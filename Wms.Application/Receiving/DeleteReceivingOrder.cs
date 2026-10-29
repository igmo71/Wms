using System.Text.Json;
using Wms.Application.Authorization;
using Wms.Application.CommandExecution;
using Wms.Application.Inventory;
using Wms.Common;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Application.Receiving;

public sealed record DeleteReceivingOrderCommand(
    Guid RequestId, Guid OrderId, string SnapshotToken,
    bool ConfirmNegative, string UserId);

public sealed class DeleteReceivingOrder(
    CommandExecutor executor, IReceivingOrderDeletionStore store,
    PostInventoryMovement.Handler posting, WarehousePermission permission,
    TimeProvider clock)
{
    private const string CommandType = "receiving.order.delete";

    public Task<OperationResult<Guid>> ExecuteAsync(
        DeleteReceivingOrderCommand command, CancellationToken ct = default)
    {
        if (command is null || command.OrderId == Guid.Empty
            || string.IsNullOrWhiteSpace(command.SnapshotToken)
            || command.SnapshotToken.Length != 64)
            return Task.FromResult<OperationResult<Guid>>(
                OperationError.Invalid("Для удаления нужны ордер и подтверждённый предпросмотр."));

        var hash = CommandExecutor.ComputeHash(JsonSerializer.Serialize(new
        {
            command.OrderId, command.SnapshotToken, command.ConfirmNegative
        }));
        return executor.ExecuteAsync(CommandType, command.RequestId, hash, command.UserId,
            async (session, operationCt) =>
            {
                var access = await permission.RequireManagerAsync(command.UserId, operationCt);
                if (!access.IsSuccess)
                    return access.Error!;
                var state = await store.LoadForDeletionAsync(
                    session, command.OrderId, operationCt);
                if (state is null)
                    return OperationError.NotFound("Приходный ордер не найден.");
                var calculated = ReceivingOrderDeletionPreviewFactory.Create(state);
                if (!calculated.IsSuccess)
                    return calculated.Error!;
                var preview = calculated.Value!;
                if (!string.Equals(preview.SnapshotToken, command.SnapshotToken,
                        StringComparison.Ordinal))
                    return OperationError.Conflict(
                        "Ордер, LPN или остатки изменились после предпросмотра. Обновите прогноз.");
                if (preview.RequiresNegativeConfirmation && !command.ConfirmNegative)
                    return OperationError.Conflict(
                        "Прогноз содержит отрицательный остаток. Подтвердите его явно.");

                var order = state.Order;
                var now = clock.GetUtcNow();
                var oldStatus = order.Status;
                var activeLinks = order.SourceLinks
                    .Where(x => x.DetachedAtUtc is null).ToArray();
                foreach (var lpn in order.LicensePlateNumbers.OrderBy(x => x.Id))
                {
                    if (lpn.Status == LicensePlateNumberStatus.Deleted)
                        continue;
                    OperationResult deleted = lpn.Status == LicensePlateNumberStatus.Closed
                        ? order.DeleteClosedReceivingLpn(lpn,
                            lpn.Items.Any(x => !x.IsDeleted
                                && state.ZeroConfirmedSkuIds.Contains(x.StockKeepingUnitId)),
                            command.UserId, now)
                        : lpn.DeleteWithReceivingOrder(command.UserId, now);
                    if (!deleted.IsSuccess)
                        return deleted.Error!;
                }

                var originals = state.Movements.Where(x => x.ReversedMovementId is null)
                    .ToDictionary(x => x.Id);
                var reversalIds = new List<Guid>(preview.Reversals.Count);
                foreach (var reversal in preview.Reversals)
                {
                    var movement = InventoryMovement.CreateReversal(
                        Guid.NewGuid(), originals[reversal.OriginalMovementId], now,
                        RecorderType.ReceivingOrder, order.Id, 1, command.UserId);
                    if (!movement.IsSuccess)
                        return movement.Error!;
                    var posted = await posting.HandleAsync(new PostInventoryMovement.Command(
                        movement.Value!, InventoryPostingKind.Reversal, now),
                        session, operationCt);
                    if (!posted.IsSuccess)
                        return posted.Error!;
                    reversalIds.Add(movement.Value!.Id);
                }

                var auditEvents = new List<WarehouseAuditEvent>(activeLinks.Length + 1);
                foreach (var link in activeLinks)
                {
                    var detached = link.Detach(now);
                    if (!detached.IsSuccess)
                        return detached.Error!;
                    var sourceAudit = WarehouseAuditEvent.Create(
                        "receiving.order.source-detached", now, command.UserId,
                        null, order.Id, null, null, link.ReceivingSourceDocumentId,
                        JsonSerializer.Serialize(new { Attached = true }),
                        JsonSerializer.Serialize(new { Attached = false,
                            Reason = "OrderDeleted" }),
                        commandUserId: command.UserId, commandType: CommandType,
                        requestId: command.RequestId);
                    if (!sourceAudit.IsSuccess)
                        return sourceAudit.Error!;
                    auditEvents.Add(sourceAudit.Value!);
                }
                var clearedPlan = order.ApplyCurrentSourcePlan(
                    new Dictionary<Guid, decimal?>());
                if (!clearedPlan.IsSuccess)
                    return clearedPlan.Error!;
                var deletedOrder = order.Delete(command.UserId, now);
                if (!deletedOrder.IsSuccess)
                    return deletedOrder.Error!;

                var audit = WarehouseAuditEvent.Create("receiving.order.deleted", now,
                    command.UserId, null, order.Id, null, null, null,
                    JsonSerializer.Serialize(new
                    {
                        Status = oldStatus.ToString(), LpnCount = preview.Lpns.Count,
                        ActiveSourceCount = activeLinks.Length
                    }),
                    JsonSerializer.Serialize(new
                    {
                        Status = "Deleted", ReversalCount = reversalIds.Count,
                        NegativeConfirmed = preview.RequiresNegativeConfirmation,
                        AffectedBalanceCount = preview.Balances.Count
                    }), reversalIds, commandUserId: command.UserId,
                    commandType: CommandType, requestId: command.RequestId);
                if (!audit.IsSuccess)
                    return audit.Error!;
                auditEvents.Add(audit.Value!);
                return new CommandExecutionResult(order.Id, auditEvents);
            }, ct);
    }
}

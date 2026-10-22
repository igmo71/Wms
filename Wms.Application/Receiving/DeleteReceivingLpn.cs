using System.Text.Json;
using Wms.Application.Authorization;
using Wms.Application.CommandExecution;
using Wms.Application.Inventory;
using Wms.Common;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Application.Receiving;

public sealed record DeleteReceivingLpnCommand(
    Guid RequestId, Guid OrderId, Guid LpnId, string SnapshotToken,
    bool ConfirmNegative, string UserId);

public sealed class DeleteReceivingLpn(
    CommandExecutor executor, IReceivingLpnDeletionStore store,
    PostInventoryMovement.Handler posting, WarehousePermission permission,
    TimeProvider clock)
{
    private const string CommandType = "receiving.lpn.delete";

    public Task<OperationResult<Guid>> ExecuteAsync(
        DeleteReceivingLpnCommand command, CancellationToken ct = default)
    {
        if (command is null || command.OrderId == Guid.Empty || command.LpnId == Guid.Empty
            || string.IsNullOrWhiteSpace(command.SnapshotToken)
            || command.SnapshotToken.Length != 64)
            return Task.FromResult<OperationResult<Guid>>(
                OperationError.Invalid("Для удаления нужны ордер, LPN и подтверждённый предпросмотр."));

        var hash = CommandExecutor.ComputeHash(JsonSerializer.Serialize(new
        {
            command.OrderId, command.LpnId, command.SnapshotToken,
            command.ConfirmNegative
        }));
        return executor.ExecuteAsync(CommandType, command.RequestId, hash, command.UserId,
            async (session, operationCt) =>
            {
                var access = await permission.RequireManagerAsync(command.UserId, operationCt);
                if (!access.IsSuccess)
                    return access.Error!;
                var state = await store.LoadForDeletionAsync(
                    session, command.OrderId, command.LpnId, operationCt);
                if (state is null)
                    return OperationError.NotFound("LPN не найден в ордере.");
                var calculated = ReceivingLpnDeletionPreviewFactory.Create(state);
                if (!calculated.IsSuccess)
                    return calculated.Error!;
                var preview = calculated.Value!;
                if (!string.Equals(preview.SnapshotToken, command.SnapshotToken,
                        StringComparison.Ordinal))
                    return OperationError.Conflict(
                        "Движения или остатки изменились после предпросмотра. Обновите прогноз.");
                if (preview.RequiresNegativeConfirmation && !command.ConfirmNegative)
                    return OperationError.Conflict(
                        "Прогноз содержит отрицательный остаток. Подтвердите его явно.");

                var order = state.Order;
                var lpn = state.Lpn;
                var item = lpn.Items.Single(x => !x.IsDeleted);
                var orderItem = order.Items.Single(x =>
                    x.StockKeepingUnitId == item.StockKeepingUnitId);
                var oldFact = orderItem.FactQuantity;
                var now = clock.GetUtcNow();
                var deleted = order.DeleteClosedReceivingLpn(
                    lpn, state.ZeroPreviouslyConfirmed, command.UserId, now);
                if (!deleted.IsSuccess)
                    return deleted.Error!;

                var originals = state.Movements.Where(x => x.ReversedMovementId is null)
                    .ToDictionary(x => x.Id);
                var reversalIds = new List<Guid>(preview.Reversals.Count);
                foreach (var reversal in preview.Reversals)
                {
                    var movement = InventoryMovement.CreateReversal(
                        Guid.NewGuid(), originals[reversal.OriginalMovementId], now,
                        RecorderType.ReceivingOrder, order.Id,
                        1, command.UserId);
                    if (!movement.IsSuccess)
                        return movement.Error!;
                    var posted = await posting.HandleAsync(new PostInventoryMovement.Command(
                        movement.Value!, InventoryPostingKind.Reversal, now),
                        session, operationCt);
                    if (!posted.IsSuccess)
                        return posted.Error!;
                    reversalIds.Add(movement.Value!.Id);
                }

                var audit = WarehouseAuditEvent.Create("receiving.lpn.deleted", now,
                    command.UserId, null, order.Id, lpn.Id, item.StockKeepingUnitId, null,
                    JsonSerializer.Serialize(new
                    {
                        Status = "Closed", Quantity = item.Quantity, Fact = oldFact
                    }),
                    JsonSerializer.Serialize(new
                    {
                        Status = "Deleted", Fact = deleted.Value,
                        NegativeConfirmed = preview.RequiresNegativeConfirmation,
                        AffectedBalanceCount = preview.Balances.Count
                    }),
                    reversalIds, commandUserId: command.UserId,
                    commandType: CommandType, requestId: command.RequestId);
                if (!audit.IsSuccess)
                    return audit.Error!;
                return new CommandExecutionResult(lpn.Id, [audit.Value!]);
            }, ct);
    }
}

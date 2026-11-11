using System.Text.Json;
using Wms.Application.Authorization;
using Wms.Application.CommandExecution;
using Wms.Common;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Application.Inventory;

public sealed record ResolveStockShortageRequest(Guid RequestId, Guid ShortageId,
    bool ShortageConfirmed, string Reason, string UserId);

public sealed class ResolveStockShortage(CommandExecutor executor,
    IStockShortageStore shortages, PostInventoryMovement.Handler posting,
    WarehousePermission permission, TimeProvider clock)
{
    private const string CommandType = "inventory.shortage.resolve";

    public Task<OperationResult<Guid>> ExecuteAsync(
        ResolveStockShortageRequest request, CancellationToken ct = default)
    {
        if (request is null || request.RequestId == Guid.Empty
            || request.ShortageId == Guid.Empty || string.IsNullOrWhiteSpace(request.UserId)
            || string.IsNullOrWhiteSpace(request.Reason)
            || request.Reason.Trim().Length > StockShortage.MaximumCommentLength)
            return Task.FromResult<OperationResult<Guid>>(
                OperationError.Invalid("Укажите недостачу, решение, причину и руководителя."));
        var reason = request.Reason.Trim();
        var hash = CommandExecutor.ComputeHash(
            $"{request.ShortageId:N}:{request.ShortageConfirmed}:{reason}");
        return executor.ExecuteAsync(CommandType, request.RequestId, hash, request.UserId,
            async (session, token) =>
            {
                var access = await permission.RequireManagerAsync(request.UserId, token);
                if (!access.IsSuccess) return access.Error!;
                var state = await shortages.LoadForResolutionAsync(session,
                    request.ShortageId, token);
                if (state is null)
                    return OperationError.NotFound("Сообщение о недостаче не найдено.");
                var shortage = state.Shortage;
                if (shortage.ResolvedAtUtc is not null)
                    return OperationError.Conflict("Недостача уже разобрана.");
                var now = clock.GetUtcNow();
                var blockedQuantity = shortage.BlockedQuantity;
                Guid? movementId = null;
                if (request.ShortageConfirmed)
                {
                    var movement = InventoryMovement.Create(Guid.NewGuid(),
                        shortage.WarehouseId, shortage.StorageLocationId, null,
                        shortage.StockKeepingUnitId, blockedQuantity,
                        now, RecorderType.StockCorrection, shortage.Id, 1,
                        request.UserId, shortage.ExpirationDate);
                    if (!movement.IsSuccess) return movement.Error!;
                    var posted = await posting.HandleAsync(new PostInventoryMovement.Command(
                        movement.Value!, InventoryPostingKind.StockCorrection, now,
                        ShortageId: shortage.Id,
                        CorrectionHoldOwner: shortage.Origin == StockShortageOrigin.Picking
                            ? new InventoryPostingOwner(
                                StorageLocationLockOwnerType.PickingAssembly,
                                shortage.OriginId)
                            : new InventoryPostingOwner(
                                StorageLocationLockOwnerType.ReplenishmentTask,
                                shortage.OriginId)), session, token);
                    if (!posted.IsSuccess) return posted.Error!;
                    movementId = posted.Value;
                }
                else
                    state.Location.AdvanceOperationalRevision();
                var resolved = shortage.Resolve(request.UserId, now, reason, movementId);
                if (!resolved.IsSuccess) return resolved.Error!;
                var audit = WarehouseAuditEvent.Create("inventory.shortage.resolved",
                    now, request.UserId, null, null, null,
                    shortage.StockKeepingUnitId, null,
                    JsonSerializer.Serialize(new { ShortageId = shortage.Id,
                        BlockedQuantity = blockedQuantity }),
                    JsonSerializer.Serialize(new { ShortageId = shortage.Id,
                        Confirmed = request.ShortageConfirmed, Reason = reason,
                        MovementId = movementId }),
                    movementId is Guid id ? new[] { id } : Array.Empty<Guid>(),
                    commandUserId: request.UserId, commandType: CommandType,
                    requestId: request.RequestId,
                    pickingAssemblyId: shortage.Origin == StockShortageOrigin.Picking
                        ? shortage.OriginId : null);
                if (!audit.IsSuccess) return audit.Error!;
                return new CommandExecutionResult(shortage.Id, [audit.Value!]);
            }, ct);
    }
}

using System.Globalization;
using System.Text.Json;
using Wms.Application.Authorization;
using Wms.Application.CommandExecution;
using Wms.Application.Inventory;
using Wms.Common;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Application.Replenishment;

public sealed record CorrectReplenishmentTransferRequest(
    Guid RequestId, Guid TransferId, decimal ExpectedEffectiveQuantity,
    decimal CorrectedQuantity, string Reason, string UserId);

public sealed class CorrectReplenishmentTransfer(
    CommandExecutor executor, IReplenishmentStore replenishments,
    PostInventoryMovement.Handler posting, WarehousePermission permission,
    TimeProvider clock)
{
    private const string CommandType = "replenishment.transfer.correct";

    public Task<OperationResult<Guid>> ExecuteAsync(
        CorrectReplenishmentTransferRequest request, CancellationToken ct = default)
    {
        if (request is null || request.RequestId == Guid.Empty
            || request.TransferId == Guid.Empty
            || !WarehouseQuantity.IsNonNegative(request.ExpectedEffectiveQuantity)
            || !WarehouseQuantity.IsNonNegative(request.CorrectedQuantity)
            || string.IsNullOrWhiteSpace(request.Reason)
            || request.Reason.Trim().Length > 1000
            || string.IsNullOrWhiteSpace(request.UserId) || request.UserId.Length > 36)
            return Task.FromResult<OperationResult<Guid>>(
                OperationError.Invalid("Укажите шаг, новое количество, причину и руководителя."));
        var reason = request.Reason.Trim();
        var hash = CommandExecutor.ComputeHash(
            $"{request.TransferId:N}:"
            + request.ExpectedEffectiveQuantity.ToString(CultureInfo.InvariantCulture)
            + ":" + request.CorrectedQuantity.ToString(CultureInfo.InvariantCulture)
            + ":" + reason);
        return executor.ExecuteAsync(CommandType, request.RequestId, hash,
            request.UserId, async (session, token) =>
            {
                var access = await permission.RequireManagerAsync(request.UserId, token);
                if (!access.IsSuccess) return access.Error!;
                var state = await replenishments.LoadTransferCorrectionAsync(session,
                    request.TransferId, token);
                if (state is null)
                    return OperationError.NotFound("Проведённый шаг Подпитки не найден.");
                if (state.EffectiveQuantity != request.ExpectedEffectiveQuantity)
                    return OperationError.Conflict(
                        "Количество шага изменилось. Обновите данные перед корректировкой.");
                var change = request.CorrectedQuantity - state.EffectiveQuantity;
                if (change == 0)
                    return OperationError.Invalid("Новое количество должно отличаться от текущего.");

                var task = state.Task;
                var transfer = state.Transfer;
                var sourceId = change > 0
                    ? state.Cycle.SourceStorageLocationId
                    : transfer.DestinationStorageLocationId;
                var destinationId = change > 0
                    ? transfer.DestinationStorageLocationId
                    : state.Cycle.SourceStorageLocationId;
                var now = clock.GetUtcNow();
                var movement = InventoryMovement.Create(Guid.NewGuid(),
                    task.WarehouseId, sourceId, destinationId,
                    task.StockKeepingUnitId, Math.Abs(change), now,
                    RecorderType.StockCorrection, transfer.Id,
                    state.CorrectionCount + 1, request.UserId,
                    transfer.ExpirationDate);
                if (!movement.IsSuccess) return movement.Error!;
                var posted = await posting.HandleAsync(new PostInventoryMovement.Command(
                    movement.Value!, InventoryPostingKind.StockCorrection, now,
                    CorrectionHoldOwner: new InventoryPostingOwner(
                        StorageLocationLockOwnerType.ReplenishmentTask, task.Id)),
                    session, token);
                if (!posted.IsSuccess) return posted.Error!;
                var corrected = task.CorrectTransfer(transfer.Id, change, now);
                if (!corrected.IsSuccess) return corrected.Error!;

                var audit = WarehouseAuditEvent.Create(
                    "replenishment.transfer.corrected", now, request.UserId,
                    null, null, null, task.StockKeepingUnitId, null,
                    JsonSerializer.Serialize(new { TaskId = task.Id,
                        TransferId = transfer.Id,
                        Quantity = state.EffectiveQuantity }),
                    JsonSerializer.Serialize(new { TaskId = task.Id,
                        TransferId = transfer.Id, Quantity = request.CorrectedQuantity,
                        Reason = reason, MovementId = movement.Value!.Id,
                        TaskTransferredQuantity = task.TransferredQuantity }),
                    movementIds: [movement.Value!.Id],
                    commandUserId: request.UserId, commandType: CommandType,
                    requestId: request.RequestId,
                    pickingAssemblyId: task.PickingAssemblyId);
                if (!audit.IsSuccess) return audit.Error!;
                return new CommandExecutionResult(movement.Value!.Id, [audit.Value!]);
            }, ct);
    }
}

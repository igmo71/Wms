using System.Globalization;
using System.Text.Json;
using Wms.Application.Authorization;
using Wms.Application.CommandExecution;
using Wms.Application.Inventory;
using Wms.Common;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Application.Replenishment;

public sealed record ConfirmReplenishmentTransferRequest(
    Guid RequestId, Guid ReplenishmentTaskId, Guid SourceCycleId,
    string DestinationLocationCode, Guid StockKeepingUnitId,
    DateOnly? ExpirationDate, decimal Quantity, string UserId);

public sealed class ConfirmReplenishmentTransfer(
    CommandExecutor executor, IReplenishmentStore replenishments,
    IStockAvailabilityStore stock, PostInventoryMovement.Handler posting,
    WarehousePermission permission, TimeProvider clock)
{
    private const string CommandType = "replenishment.transfer.confirm";

    public Task<OperationResult<Guid>> ExecuteAsync(
        ConfirmReplenishmentTransferRequest request, CancellationToken ct = default)
    {
        if (request is null || request.RequestId == Guid.Empty
            || request.ReplenishmentTaskId == Guid.Empty
            || request.SourceCycleId == Guid.Empty
            || request.StockKeepingUnitId == Guid.Empty
            || !WarehouseQuantity.IsPositive(request.Quantity)
            || string.IsNullOrWhiteSpace(request.UserId)
            || request.UserId.Length > 36)
            return Task.FromResult<OperationResult<Guid>>(
                OperationError.Invalid("Укажите задание, снятый поддон, SKU, количество и подпитчика."));
        var normalized = StorageLocationAddress.Normalize(request.DestinationLocationCode);
        if (!normalized.IsSuccess)
            return Task.FromResult<OperationResult<Guid>>(normalized.Error!);
        var code = normalized.Value!;
        var hash = CommandExecutor.ComputeHash(
            $"{request.ReplenishmentTaskId:N}:{request.SourceCycleId:N}:{code}:" +
            $"{request.StockKeepingUnitId:N}:{request.ExpirationDate:yyyy-MM-dd}:" +
            request.Quantity.ToString(CultureInfo.InvariantCulture));
        return executor.ExecuteAsync(CommandType, request.RequestId, hash,
            request.UserId, async (session, token) =>
            {
                var access = await permission.RequireOperatorAsync(request.UserId, token);
                if (!access.IsSuccess) return access.Error!;
                var state = await replenishments.LoadForTransferAsync(session,
                    request.ReplenishmentTaskId, code, token);
                var task = state.Task;
                if (task is null)
                    return OperationError.NotFound("Задание Подпитки не найдено.");
                if (task.Status != ReplenishmentTaskStatus.InProgress
                    || task.AssignedTo != request.UserId.Trim()
                    || request.StockKeepingUnitId != task.StockKeepingUnitId
                    || request.Quantity > task.RemainingQuantity)
                    return OperationError.Conflict(
                        "Задание, исполнитель, SKU или оставшееся количество изменились.");
                var cycle = task.SourceCycles.SingleOrDefault(x =>
                    x.Id == request.SourceCycleId && x.CompletedAtUtc is null);
                if (cycle is null)
                    return OperationError.Conflict("Снятый поддон уже возвращён или не принадлежит заданию.");
                var source = state.Source;
                if (source is null || source.Id != cycle.SourceStorageLocationId
                    || source.IsFolder || source.DeletionMark
                    || source.WarehouseId != task.WarehouseId
                    || source.Warehouse?.DeletionMark != false
                    || source.Zone is null || source.Zone.DeletionMark
                    || source.Zone.WarehouseId != task.WarehouseId
                    || source.Zone.Type != ZoneType.Reserve
                    || source.ActiveLock?.OwnerType != StorageLocationLockOwnerType.ReplenishmentTask
                    || source.ActiveLock?.OwnerId != task.Id)
                    return OperationError.Conflict(
                        "Исходный поддон больше не удерживается этим заданием.");
                var destination = state.Destination;
                if (destination is null || destination.IsFolder || destination.DeletionMark
                    || destination.Warehouse?.DeletionMark != false
                    || destination.Zone is null || destination.Zone.DeletionMark
                    || destination.Zone.WarehouseId != task.WarehouseId
                    || destination.Zone.Type != ZoneType.Storage
                    || destination.ActiveLock is not null)
                    return OperationError.Conflict(
                        "Отсканируйте свободную активную ячейку Комплектации этого склада.");
                if (!destination.AllowsMultipleSkus
                    && state.DestinationSkuIds.Any(x => x != task.StockKeepingUnitId))
                    return OperationError.Conflict(
                        "В выбранной ячейке Комплектации запрещено смешение SKU.");

                var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
                var available = await stock.ReadSkuAsync(session, task.WarehouseId,
                    task.StockKeepingUnitId, token);
                var groups = available.Where(x =>
                        x.Group.StorageLocationId == source.Id
                        && x.AvailableForReplenishment(task.Id) > 0
                        && (x.Group.ExpirationDate is null
                            || x.Group.ExpirationDate >= today))
                    .OrderBy(x => x.Group.ExpirationDate is null)
                    .ThenBy(x => x.Group.ExpirationDate).ToArray();
                var chosen = groups.FirstOrDefault(x =>
                    x.Group.ExpirationDate == request.ExpirationDate);
                if (chosen is null
                    || chosen.AvailableForReplenishment(task.Id) < request.Quantity)
                    return OperationError.Conflict(
                        "В снятом поддоне недостаточно доступного товара выбранного срока.");
                if (groups[0].Group.ExpirationDate != request.ExpirationDate)
                    return OperationError.Conflict(
                        "Сначала перенесите товар с более ранним сроком годности.");

                var now = clock.GetUtcNow();
                var movement = InventoryMovement.Create(Guid.NewGuid(),
                    task.WarehouseId, source.Id, destination.Id,
                    task.StockKeepingUnitId, request.Quantity, now,
                    RecorderType.ReplenishmentTask, task.Id,
                    task.SourceCycles.Sum(x => x.Transfers.Count) + 1,
                    request.UserId, request.ExpirationDate);
                if (!movement.IsSuccess) return movement.Error!;
                var transfer = task.ConfirmTransfer(Guid.NewGuid(), cycle.Id,
                    destination.Id, request.ExpirationDate, request.Quantity,
                    movement.Value!.Id, request.UserId, now);
                if (!transfer.IsSuccess) return transfer.Error!;
                session.Stage(transfer.Value!);
                var posted = await posting.HandleAsync(new PostInventoryMovement.Command(
                    movement.Value!, InventoryPostingKind.Replenishment, now),
                    session, token);
                if (!posted.IsSuccess) return posted.Error!;
                var audit = WarehouseAuditEvent.Create(
                    "replenishment.transfer.confirmed", now, request.UserId,
                    null, null, null, task.StockKeepingUnitId, null, null,
                    JsonSerializer.Serialize(new { TaskId = task.Id,
                        CycleId = cycle.Id, TransferId = transfer.Value!.Id,
                        SourceLocationId = source.Id,
                        DestinationLocationId = destination.Id,
                        request.ExpirationDate, request.Quantity,
                        task.TransferredQuantity }),
                    movementIds: [movement.Value!.Id],
                    commandUserId: request.UserId, commandType: CommandType,
                    requestId: request.RequestId,
                    pickingAssemblyId: task.PickingAssemblyId);
                if (!audit.IsSuccess) return audit.Error!;
                return new CommandExecutionResult(transfer.Value!.Id, [audit.Value!]);
            }, ct);
    }
}

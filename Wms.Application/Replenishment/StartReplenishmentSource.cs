using System.Text.Json;
using Wms.Application.Authorization;
using Wms.Application.CommandExecution;
using Wms.Application.Inventory;
using Wms.Common;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Application.Replenishment;

public sealed record StartReplenishmentSourceRequest(Guid RequestId,
    Guid ReplenishmentTaskId, string SourceLocationCode, string UserId);

public sealed class StartReplenishmentSource(
    CommandExecutor executor, IReplenishmentStore replenishments,
    IStockAvailabilityStore stock, IStockReservationStore reservations,
    WarehousePermission permission, TimeProvider clock)
{
    private const string CommandType = "replenishment.source.start";

    public Task<OperationResult<Guid>> ExecuteAsync(
        StartReplenishmentSourceRequest request, CancellationToken ct = default)
    {
        if (request is null || request.RequestId == Guid.Empty
            || request.ReplenishmentTaskId == Guid.Empty
            || string.IsNullOrWhiteSpace(request.UserId) || request.UserId.Length > 36)
            return Task.FromResult<OperationResult<Guid>>(
                OperationError.Invalid("Укажите задание, ячейку и подпитчика."));
        var normalized = StorageLocationAddress.Normalize(request.SourceLocationCode);
        if (!normalized.IsSuccess)
            return Task.FromResult<OperationResult<Guid>>(normalized.Error!);
        var code = normalized.Value!;
        var hash = CommandExecutor.ComputeHash($"{request.ReplenishmentTaskId:N}:{code}");
        return executor.ExecuteAsync(CommandType, request.RequestId, hash,
            request.UserId, async (session, token) =>
            {
                var access = await permission.RequireOperatorAsync(request.UserId, token);
                if (!access.IsSuccess) return access.Error!;
                var state = await replenishments.LoadForSourceStartAsync(session,
                    request.ReplenishmentTaskId, code, token);
                var task = state.Task;
                if (task is null)
                    return OperationError.NotFound("Задание Подпитки не найдено.");
                if (task.Status is ReplenishmentTaskStatus.Completed
                    or ReplenishmentTaskStatus.Cancelled
                    || task.RemainingQuantity <= 0
                    || task.SourceCycles.Any(x => x.CompletedAtUtc is null))
                    return OperationError.Conflict(
                        "Задание завершено или сначала нужно вернуть уже снятый поддон.");
                if (task.AssignedTo is not null && task.AssignedTo != request.UserId.Trim())
                    return OperationError.Conflict(
                        "Задание выполняется другим подпитчиком.");
                if (task.Status == ReplenishmentTaskStatus.Ordered
                    && state.Assembly?.Status != PickingAssemblyStatus.InProgress)
                    return OperationError.Conflict(
                        "Не начатая Подпитка больше не относится к активной сборке.");
                var location = state.Location;
                if (location is null || location.IsFolder || location.DeletionMark
                    || location.Warehouse?.DeletionMark != false
                    || location.Zone is null || location.Zone.DeletionMark
                    || location.Zone.WarehouseId != task.WarehouseId
                    || location.Zone.Type != ZoneType.Reserve)
                    return OperationError.Conflict(
                        "Для Подпитки нужна активная ячейка Хранения того же склада.");
                if (location.ActiveLock is not null)
                    return OperationError.Conflict(
                        "Ячейка уже удержана другим складским действием.");

                var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
                var available = await stock.ReadSkuAsync(session, task.WarehouseId,
                    task.StockKeepingUnitId, token);
                var cell = available.Where(x => x.Group.StorageLocationId == location.Id
                    && x.AccountingQuantity > 0).ToArray();
                if (cell.Length == 0 || cell.Any(x => x.ReservedQuantity > 0)
                    || cell.All(x => x.AvailableQuantity <= 0
                        || x.Group.ExpirationDate is DateOnly date && date < today))
                    return OperationError.Conflict(
                        "В исходной ячейке нет свободного товара нужного SKU и допустимого срока.");

                var proposalState = await replenishments.LoadProposalStateAsync(session,
                    task.WarehouseId, [task.StockKeepingUnitId], token);
                var locations = proposalState.Locations.ToDictionary(x => x.Id);
                var reservedCells = await replenishments.ReadReservedLocationIdsAsync(
                    session, task.WarehouseId,
                    locations.Values.Where(x => x.ZoneType == ZoneType.Reserve)
                        .Select(x => x.Id).ToArray(), token);
                if (reservedCells.Contains(location.Id))
                    return OperationError.Conflict(
                        "В исходной ячейке есть товар, зарезервированный для Отбора.");
                var eligibleCells = available.GroupBy(x => x.Group.StorageLocationId)
                    .Where(group => !reservedCells.Contains(group.Key))
                    .Select(group => group.Key).ToHashSet();
                var sourceGroups = available.Where(x => x.AvailableQuantity > 0
                    && (x.Group.ExpirationDate is null
                        || x.Group.ExpirationDate >= today)
                    && eligibleCells.Contains(x.Group.StorageLocationId)
                    && locations.TryGetValue(x.Group.StorageLocationId, out var candidate)
                    && candidate.IsActive && !candidate.IsHeld
                    && candidate.ZoneType == ZoneType.Reserve).ToArray();
                var earliest = sourceGroups.OrderBy(x => x.Group.ExpirationDate is null)
                    .ThenBy(x => x.Group.ExpirationDate).FirstOrDefault();
                var chosen = sourceGroups.Where(x => x.Group.StorageLocationId == location.Id)
                    .OrderBy(x => x.Group.ExpirationDate is null)
                    .ThenBy(x => x.Group.ExpirationDate).FirstOrDefault();
                if (chosen is null || earliest is null
                    || chosen.Group.ExpirationDate != earliest.Group.ExpirationDate)
                    return OperationError.Conflict(
                        "Выбранный источник нарушает очередность сроков годности.");

                var now = clock.GetUtcNow();
                var hold = StorageLocationLock.CreateForReplenishment(location.Id,
                    task.Id, now, request.UserId);
                if (!hold.IsSuccess) return hold.Error!;
                var held = await reservations.HoldAsync(session, task.WarehouseId,
                    hold.Value!, token);
                if (!held.IsSuccess) return held.Error!;
                var cycle = task.StartSource(Guid.NewGuid(), location.Id,
                    request.UserId, now);
                if (!cycle.IsSuccess) return cycle.Error!;
                session.Stage(cycle.Value!);

                // A scan may replace the original recommendation. Keep all
                // other orders and redistribute only their suggested sources.
                var ordered = (await replenishments.LoadOrderedTasksAsync(session,
                    task.WarehouseId, task.StockKeepingUnitId, token))
                    .Where(x => x.Id != task.Id).ToArray();
                var candidates = sourceGroups.Where(x =>
                        x.Group.StorageLocationId != location.Id)
                    .GroupBy(x => x.Group.StorageLocationId)
                    .Select(x => new SourceCandidate(x.Key,
                        x.OrderBy(g => g.Group.ExpirationDate is null)
                            .ThenBy(g => g.Group.ExpirationDate)
                            .First().Group.ExpirationDate,
                        x.Sum(g => g.AvailableQuantity)))
                    .OrderBy(x => x.ExpirationDate is null)
                    .ThenBy(x => x.ExpirationDate)
                    .ThenByDescending(x => x.Available)
                    .ThenBy(x => locations[x.LocationId].Code, StringComparer.Ordinal)
                    .ToArray();
                foreach (var other in ordered)
                {
                    var candidate = candidates.FirstOrDefault(x => x.Available > 0);
                    var updated = other.UpdateRecommendedSource(candidate?.LocationId, now);
                    if (!updated.IsSuccess) return updated.Error!;
                    if (candidate is not null)
                        candidate.Available -= Math.Min(candidate.Available,
                            other.RemainingQuantity);
                }

                var audit = WarehouseAuditEvent.Create(
                    "replenishment.source.started", now, request.UserId,
                    null, null, null, task.StockKeepingUnitId, null, null,
                    JsonSerializer.Serialize(new { TaskId = task.Id,
                        CycleId = cycle.Value!.Id, SourceLocationId = location.Id,
                        UpdatedRecommendations = ordered.Length }),
                    commandUserId: request.UserId, commandType: CommandType,
                    requestId: request.RequestId,
                    pickingAssemblyId: task.PickingAssemblyId);
                if (!audit.IsSuccess) return audit.Error!;
                return new CommandExecutionResult(cycle.Value!.Id, [audit.Value!]);
            }, ct);
    }

    private sealed class SourceCandidate(Guid locationId,
        DateOnly? expirationDate, decimal available)
    {
        public Guid LocationId { get; } = locationId;
        public DateOnly? ExpirationDate { get; } = expirationDate;
        public decimal Available { get; set; } = available;
    }
}

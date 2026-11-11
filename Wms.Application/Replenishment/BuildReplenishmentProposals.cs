using Wms.Application.Inventory;
using Wms.Application.Picking;
using Wms.Common;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Application.Replenishment;

public sealed record ReplenishmentLocation(
    Guid Id, string Code, ZoneType ZoneType, bool IsActive, bool IsHeld,
    bool AllowsMultipleSkus, IReadOnlySet<Guid> CurrentSkuIds,
    IReadOnlySet<Guid> HistoricalSkuIds);

public sealed record ReplenishmentProposalRequest(
    Guid PickingAssemblyId, Guid WarehouseId, DateOnly Today,
    IReadOnlyDictionary<Guid, decimal> RemainingBySku,
    IReadOnlyList<PickingRouteStep> RouteSteps,
    IReadOnlyList<StockAvailability> Availability,
    IReadOnlyList<ReplenishmentLocation> Locations,
    IReadOnlyList<ReplenishmentTask> ExistingTasks);

public sealed record ReplenishmentProposalGroup(DateOnly? ExpirationDate,
    decimal Quantity);

public sealed record ReplenishmentProposal(Guid StockKeepingUnitId,
    Guid SourceLocationId, Guid? DestinationLocationId, decimal Quantity,
    IReadOnlyList<ReplenishmentProposalGroup> Groups);

public sealed record ReplenishmentDeficit(Guid StockKeepingUnitId,
    decimal Quantity);

public sealed record ReplenishmentProposalList(
    IReadOnlyList<ReplenishmentProposal> Proposals,
    IReadOnlyList<ReplenishmentDeficit> Deficits);

// Recommendations only. Ordering a task and scanning its source recheck stock.
public sealed class BuildReplenishmentProposals
{
    public OperationResult<ReplenishmentProposalList> Execute(
        ReplenishmentProposalRequest request)
    {
        if (request is null || request.PickingAssemblyId == Guid.Empty
            || request.WarehouseId == Guid.Empty || request.Today == default
            || request.RemainingBySku is null || request.RouteSteps is null
            || request.Availability is null
            || request.Locations is null || request.ExistingTasks is null
            || request.RemainingBySku.Any(x => x.Key == Guid.Empty
                || !WarehouseQuantity.IsNonNegative(x.Value))
            || request.RouteSteps.Any(x => x.StockKeepingUnitId == Guid.Empty
                || x.StorageLocationId == Guid.Empty
                || !WarehouseQuantity.IsPositive(x.Quantity))
            || request.Locations.Any(x => x.Id == Guid.Empty || x.CurrentSkuIds is null
                || x.HistoricalSkuIds is null)
            || request.Locations.Select(x => x.Id).Distinct().Count()
                != request.Locations.Count)
            return OperationError.Invalid("Для расчёта Подпитки нужны сборка, остаточная потребность и доступность.");

        var locations = request.Locations.ToDictionary(x => x.Id);
        var proposals = new List<ReplenishmentProposal>();
        var deficits = new List<ReplenishmentDeficit>();
        var activeTasks = request.ExistingTasks.Where(x =>
            x.WarehouseId == request.WarehouseId
            && x.Status is (ReplenishmentTaskStatus.Ordered
                or ReplenishmentTaskStatus.InProgress)).ToArray();

        foreach (var (skuId, required) in request.RemainingBySku
            .Where(x => x.Value > 0).OrderBy(x => x.Key))
        {
            var selected = request.RouteSteps.Where(x => x.StockKeepingUnitId == skuId)
                .Sum(x => x.Quantity);
            var needed = required - selected
                - activeTasks.Where(x => x.PickingAssemblyId == request.PickingAssemblyId
                    && x.StockKeepingUnitId == skuId).Sum(x => x.RemainingQuantity);
            if (needed <= 0) continue;

            var stockGroups = request.Availability.Where(x =>
                    x.Group.WarehouseId == request.WarehouseId
                    && x.Group.StockKeepingUnitId == skuId
                    && (x.Group.ExpirationDate is null
                        || x.Group.ExpirationDate >= request.Today)
                    && x.Hold is null
                    && locations.TryGetValue(x.Group.StorageLocationId, out var location)
                    && location.IsActive && !location.IsHeld
                    && location.ZoneType is (ZoneType.Storage or ZoneType.Reserve)
                    && x.AvailableQuantity > 0)
                .Select(x => new StockGroup(x.Group.StorageLocationId,
                    x.Group.ExpirationDate, locations[x.Group.StorageLocationId].ZoneType,
                    x.AvailableQuantity))
                .OrderBy(x => x.ExpirationDate is null)
                .ThenBy(x => x.ExpirationDate)
                .ThenBy(x => x.ZoneType == ZoneType.Storage ? 0 : 1)
                .ThenByDescending(x => x.Available)
                .ThenBy(x => locations[x.LocationId].Code, StringComparer.Ordinal)
                .ToArray();

            // Route steps already account for part of the free stock. A later
            // Storage group may become usable after an earlier Reserve group is
            // replenished, even when the current route reports it as a gap.
            foreach (var step in request.RouteSteps.Where(x =>
                x.StockKeepingUnitId == skuId))
            {
                var group = stockGroups.FirstOrDefault(x =>
                    x.LocationId == step.StorageLocationId
                    && x.ExpirationDate == step.ExpirationDate);
                if (group is not null)
                    group.Available = Math.Max(0, group.Available - step.Quantity);
            }

            // An ordered task is not a stock reservation yet, but its suggested
            // source must not be offered to another task at the same time.
            foreach (var task in activeTasks.Where(x =>
                x.StockKeepingUnitId == skuId
                && x.Status == ReplenishmentTaskStatus.Ordered
                && x.RecommendedSourceStorageLocationId is not null))
            {
                var reserved = task.RemainingQuantity;
                foreach (var source in stockGroups.Where(x =>
                    x.ZoneType == ZoneType.Reserve
                    && x.LocationId == task.RecommendedSourceStorageLocationId))
                {
                    var quantity = Math.Min(reserved, source.Available);
                    source.Available -= quantity;
                    reserved -= quantity;
                    if (reserved == 0) break;
                }
            }

            var chosen = new Dictionary<Guid, List<ReplenishmentProposalGroup>>();
            foreach (var source in stockGroups.Where(x => x.Available > 0))
            {
                if (needed == 0) break;
                var quantity = Math.Min(needed, source.Available);
                if (source.ZoneType == ZoneType.Reserve)
                {
                    if (!chosen.TryGetValue(source.LocationId, out var groups))
                        chosen[source.LocationId] = groups = [];
                    groups.Add(new ReplenishmentProposalGroup(source.ExpirationDate, quantity));
                }
                needed -= quantity;
            }

            var destination = request.Locations.Where(x =>
                    x.ZoneType == ZoneType.Storage && x.IsActive && !x.IsHeld
                    && (x.AllowsMultipleSkus || x.CurrentSkuIds.Count == 0
                        || x.CurrentSkuIds.All(id => id == skuId)))
                .OrderBy(x => x.CurrentSkuIds.Contains(skuId) ? 0
                    : x.CurrentSkuIds.Count == 0 && x.HistoricalSkuIds.Contains(skuId) ? 1 : 2)
                .ThenBy(x => x.Code, StringComparer.Ordinal)
                .FirstOrDefault(x => x.CurrentSkuIds.Contains(skuId)
                    || x.CurrentSkuIds.Count == 0 && x.HistoricalSkuIds.Contains(skuId));

            foreach (var source in chosen)
                proposals.Add(new ReplenishmentProposal(skuId, source.Key,
                    destination?.Id, source.Value.Sum(x => x.Quantity), source.Value));
            if (needed > 0)
                deficits.Add(new ReplenishmentDeficit(skuId, needed));
        }

        return new ReplenishmentProposalList(proposals, deficits);
    }

    private sealed class StockGroup(Guid locationId, DateOnly? expirationDate,
        ZoneType zoneType, decimal available)
    {
        public Guid LocationId { get; } = locationId;
        public DateOnly? ExpirationDate { get; } = expirationDate;
        public ZoneType ZoneType { get; } = zoneType;
        public decimal Available { get; set; } = available;
    }
}

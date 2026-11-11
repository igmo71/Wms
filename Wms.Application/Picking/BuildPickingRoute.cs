using Wms.Application.Inventory;
using Wms.Common;
using Wms.Domain.Enums;

namespace Wms.Application.Picking;

public sealed record PickingRouteLocation(
    Guid Id, string Code, ZoneType ZoneType, bool IsActive, int DistinctSkuCount);

public sealed record PickingRouteRequest(
    Guid PickingAssemblyId, Guid WarehouseId, DateOnly Today,
    IReadOnlyDictionary<Guid, decimal> RemainingBySku,
    IReadOnlyList<StockAvailability> Availability,
    IReadOnlyList<PickingRouteLocation> Locations,
    Guid? PreviousLocationId = null, Guid? NextLocationId = null);

public sealed record PickingRouteStep(Guid StorageLocationId, string LocationCode,
    Guid StockKeepingUnitId, DateOnly? ExpirationDate, decimal Quantity,
    bool DirectPalletPick);

public sealed record PickingRouteGap(Guid StockKeepingUnitId,
    DateOnly? ExpirationDate, decimal Quantity);

public sealed record PickingRoute(
    IReadOnlyList<PickingRouteStep> Steps,
    IReadOnlyList<PickingRouteGap> Gaps);

// A recommendation over one availability snapshot. Physical confirmation repeats
// the availability, FEFO and location checks in its own command transaction.
public sealed class BuildPickingRoute
{
    public OperationResult<PickingRoute> Execute(PickingRouteRequest request)
    {
        if (request is null || request.PickingAssemblyId == Guid.Empty
            || request.WarehouseId == Guid.Empty
            || request.Today == default || request.RemainingBySku is null
            || request.Availability is null || request.Locations is null
            || request.RemainingBySku.Any(x => x.Key == Guid.Empty
                || !WarehouseQuantity.IsNonNegative(x.Value)))
            return OperationError.Invalid("Для маршрута нужны сборка, потребность и доступность.");

        var locations = request.Locations.ToDictionary(x => x.Id);
        var ordered = request.Locations.Select(x => new PickingAddress(x.Id, x.Code))
            .OrderBy(x => x, PickingAddressComparer.Instance).ToArray();
        var ranks = ordered.Select((address, index) => (address.LocationId, index))
            .ToDictionary(x => x.LocationId, x => x.index);
        int? previous = request.PreviousLocationId is Guid previousId
            && ranks.TryGetValue(previousId, out var previousRank) ? previousRank : null;
        int? next = request.NextLocationId is Guid nextId
            && ranks.TryGetValue(nextId, out var nextRank) ? nextRank : null;

        var groups = request.Availability.Where(x =>
                x.Group.WarehouseId == request.WarehouseId
                && locations.TryGetValue(x.Group.StorageLocationId, out var location)
                && location.IsActive
                && location.ZoneType is (ZoneType.Storage or ZoneType.Reserve)
                && (x.Group.ExpirationDate is null || x.Group.ExpirationDate >= request.Today)
                && x.AvailableForNewPick(request.PickingAssemblyId) > 0)
            .ToArray();
        var bySku = new List<(Guid SkuId, Queue<PickingRouteStep> Steps,
            PickingRouteGap[] Gaps)>();
        foreach (var (skuId, required) in request.RemainingBySku.Where(x => x.Value > 0))
        {
            var available = groups.Where(x => x.Group.StockKeepingUnitId == skuId).ToArray();
            var buckets = available.GroupBy(x => x.Group.ExpirationDate)
                .Select(x => new DateBucket(x.Key, x.Sum(row =>
                    row.AvailableForNewPick(request.PickingAssemblyId))))
                .OrderBy(x => x.Date is null).ThenBy(x => x.Date).ToArray();
            var demand = required;
            foreach (var bucket in buckets)
            {
                bucket.Remaining = Math.Min(demand, bucket.Remaining);
                demand -= bucket.Remaining;
            }
            var palletSteps = new List<PickingRouteStep>();
            var storageSteps = new List<PickingRouteStep>();
            var reserveCells = available.Where(x =>
                    locations[x.Group.StorageLocationId].ZoneType == ZoneType.Reserve)
                .GroupBy(x => x.Group.StorageLocationId)
                .Select(x => x.ToArray()).ToArray();

            while (true)
            {
                var pallet = reserveCells.Where(cell => IsEligiblePallet(cell,
                        request.Availability, locations[cell[0].Group.StorageLocationId],
                        buckets, request.PickingAssemblyId))
                    .OrderByDescending(cell => cell.Sum(x => x.AccountingQuantity))
                    .ThenBy(cell => NearestStorageDistance(cell[0].Group.StorageLocationId,
                        available, buckets, locations, ranks))
                    .ThenBy(cell => PickingAddressComparer.AdditionalTravel(
                        ranks[cell[0].Group.StorageLocationId], previous, next))
                    .ThenByDescending(cell => ranks[cell[0].Group.StorageLocationId])
                    .FirstOrDefault();
                if (pallet is null) break;
                foreach (var group in pallet.OrderBy(x => x.Group.ExpirationDate is null)
                    .ThenBy(x => x.Group.ExpirationDate))
                {
                    var bucket = buckets.Single(x => x.Date == group.Group.ExpirationDate);
                    bucket.Remaining -= group.AccountingQuantity;
                    palletSteps.Add(new PickingRouteStep(group.Group.StorageLocationId,
                        locations[group.Group.StorageLocationId].Code, skuId,
                        group.Group.ExpirationDate, group.AccountingQuantity, true));
                }
                reserveCells = reserveCells.Where(x =>
                    x[0].Group.StorageLocationId != pallet[0].Group.StorageLocationId).ToArray();
            }

            var gaps = new List<PickingRouteGap>();
            for (var bucketIndex = 0; bucketIndex < buckets.Length; bucketIndex++)
            {
                var bucket = buckets[bucketIndex];
                if (bucket.Remaining <= 0) continue;
                var candidates = available.Where(x => x.Group.ExpirationDate == bucket.Date
                    && locations[x.Group.StorageLocationId].ZoneType == ZoneType.Storage)
                    .ToArray();
                while (bucket.Remaining > 0 && candidates.Length > 0)
                {
                    var exact = candidates.Where(x =>
                        x.AvailableForNewPick(request.PickingAssemblyId) == bucket.Remaining)
                        .OrderBy(x => PickingAddressComparer.AdditionalTravel(
                            ranks[x.Group.StorageLocationId], previous, next))
                        .ThenByDescending(x => ranks[x.Group.StorageLocationId])
                        .FirstOrDefault();
                    var chosen = exact ?? candidates
                        .OrderBy(x => x.AvailableForNewPick(request.PickingAssemblyId))
                        .ThenBy(x => PickingAddressComparer.AdditionalTravel(
                            ranks[x.Group.StorageLocationId], previous, next))
                        .ThenByDescending(x => ranks[x.Group.StorageLocationId]).First();
                    var quantity = Math.Min(bucket.Remaining,
                        chosen.AvailableForNewPick(request.PickingAssemblyId));
                    storageSteps.Add(new PickingRouteStep(chosen.Group.StorageLocationId,
                        locations[chosen.Group.StorageLocationId].Code, skuId,
                        bucket.Date, quantity, false));
                    bucket.Remaining -= quantity;
                    candidates = candidates.Where(x => x != chosen).ToArray();
                }
                if (bucket.Remaining > 0)
                {
                    gaps.Add(new PickingRouteGap(skuId, bucket.Date, bucket.Remaining));
                    for (var later = bucketIndex + 1; later < buckets.Length; later++)
                        if (buckets[later].Remaining > 0)
                            gaps.Add(new PickingRouteGap(skuId, buckets[later].Date,
                                buckets[later].Remaining));
                    // A later expiration must wait for this shortage or replenishment.
                    break;
                }
            }
            if (demand > 0)
                gaps.Add(new PickingRouteGap(skuId, null, demand));
            var steps = palletSteps.Concat(storageSteps
                    .OrderBy(x => x.ExpirationDate is null)
                    .ThenBy(x => x.ExpirationDate)
                    .ThenByDescending(x => ranks[x.StorageLocationId])).ToArray();
            bySku.Add((skuId, new Queue<PickingRouteStep>(steps), gaps.ToArray()));
        }
        var route = new List<PickingRouteStep>();
        while (bySku.Any(x => x.Steps.Count > 0))
        {
            var nextSku = bySku.Where(x => x.Steps.Count > 0)
                .OrderByDescending(x => ranks[x.Steps.Peek().StorageLocationId])
                .ThenBy(x => x.SkuId).First();
            route.Add(nextSku.Steps.Dequeue());
        }
        return new PickingRoute(route,
            bySku.SelectMany(x => x.Gaps).ToArray());
    }

    private static bool IsEligiblePallet(StockAvailability[] cell,
        IReadOnlyList<StockAvailability> allAvailability,
        PickingRouteLocation location, DateBucket[] buckets, Guid assemblyId)
    {
        if (location.DistinctSkuCount != 1 || cell.Length == 0) return false;
        var allInCell = allAvailability.Where(x =>
            x.Group.StorageLocationId == location.Id && x.AccountingQuantity > 0).ToArray();
        if (allInCell.Length != cell.Length || cell.Any(x =>
            x.AccountingQuantity != x.AvailableForNewPick(assemblyId))) return false;
        foreach (var group in cell)
        {
            var bucket = buckets.SingleOrDefault(x => x.Date == group.Group.ExpirationDate);
            if (bucket is null || group.AccountingQuantity > bucket.Remaining)
                return false;
        }
        var latest = cell.OrderBy(x => x.Group.ExpirationDate is null)
            .ThenBy(x => x.Group.ExpirationDate).Last().Group.ExpirationDate;
        return buckets.Where(x => x.Remaining > 0 &&
                (x.Date is not null && latest is null || x.Date < latest))
            .All(bucket => cell.Where(x => x.Group.ExpirationDate == bucket.Date)
                .Sum(x => x.AccountingQuantity) == bucket.Remaining);
    }

    private static int NearestStorageDistance(Guid palletId,
        StockAvailability[] available, DateBucket[] buckets,
        IReadOnlyDictionary<Guid, PickingRouteLocation> locations,
        IReadOnlyDictionary<Guid, int> ranks)
    {
        var storageRanks = available.Where(x =>
            locations[x.Group.StorageLocationId].ZoneType == ZoneType.Storage
            && buckets.Any(bucket => bucket.Date == x.Group.ExpirationDate
                && bucket.Remaining > 0))
            .Select(x => ranks[x.Group.StorageLocationId]).ToArray();
        return storageRanks.Length == 0 ? int.MaxValue
            : storageRanks.Min(x => Math.Abs(ranks[palletId] - x));
    }

    private sealed class DateBucket(DateOnly? date, decimal remaining)
    {
        public DateOnly? Date { get; } = date;
        public decimal Remaining { get; set; } = remaining;
    }
}

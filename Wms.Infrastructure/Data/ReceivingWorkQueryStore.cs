using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Wms.Application.Receiving;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Data;

public sealed class ReceivingWorkQueryStore(
    IDbContextFactory<ApplicationDbContext> contextFactory) : IReceivingWorkQueryStore
{
    public async Task<ReceivingPutawayHistoryPage> ListPutawayHistoryAsync(
        ReceivingPutawayHistoryQuery filter, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        if (filter.DateFrom is DateTime start && filter.DateTo is DateTime end
            && start.Date > end.Date)
            return new ReceivingPutawayHistoryPage([], 0);

        var query = db.LicensePlateNumberPutaways.AsNoTracking();
        if (filter.DateFrom is DateTime dateFrom)
        {
            var fromUtc = StartOfLocalDayUtc(dateFrom);
            query = query.Where(x => x.CreatedAtUtc >= fromUtc);
        }
        if (filter.DateTo is DateTime dateTo && dateTo.Date < DateTime.MaxValue.Date)
        {
            var throughUtc = StartOfLocalDayUtc(dateTo.Date.AddDays(1));
            query = query.Where(x => x.CreatedAtUtc < throughUtc);
        }
        if (!string.IsNullOrWhiteSpace(filter.LpnCode))
        {
            var search = filter.LpnCode.Trim();
            query = query.Where(x => x.LicensePlateNumber.Code.Contains(search));
        }
        if (!string.IsNullOrWhiteSpace(filter.SkuName))
        {
            var search = filter.SkuName.Trim();
            query = query.Where(x => x.StockKeepingUnit.Name != null
                && x.StockKeepingUnit.Name.Contains(search));
        }
        if (!string.IsNullOrWhiteSpace(filter.UserName))
        {
            var search = filter.UserName.Trim();
            query = query.Where(x => x.CreatedBy.Contains(search)
                || db.Users.Any(user => user.Id == x.CreatedBy
                    && (user.DisplayName.Contains(search)
                        || user.UserName != null && user.UserName.Contains(search))));
        }
        if (!string.IsNullOrWhiteSpace(filter.DestinationAddress))
        {
            var address = StorageLocationAddress.Normalize(filter.DestinationAddress);
            if (!address.IsSuccess)
                return new ReceivingPutawayHistoryPage([], 0);
            var search = address.Value!;
            query = query.Where(x => x.DestinationStorageLocation.Code.Contains(search));
        }

        var totalItems = await query.CountAsync(ct);
        var putaways = await query
            .Include(x => x.LicensePlateNumber)
            .Include(x => x.StockKeepingUnit).ThenInclude(x => x.BaseUnitOfMeasure)
            .Include(x => x.DestinationStorageLocation).ThenInclude(x => x.Warehouse)
            .OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id)
            .Skip(Math.Max(0, filter.Skip)).Take(Math.Clamp(filter.Take, 1, 100))
            .ToArrayAsync(ct);

        return new ReceivingPutawayHistoryPage(putaways.Select(x =>
            new ReceivingPutawayHistoryRow(
                x.StockKeepingUnitId, x.CreatedAtUtc, x.CreatedBy,
                x.LicensePlateNumber.Code,
                x.StockKeepingUnit.Name, x.StockKeepingUnit.Code,
                x.Quantity,
                x.StockKeepingUnit.BaseUnitOfMeasure?.Description
                    ?? x.StockKeepingUnit.BaseUnitOfMeasure?.Abbreviation,
                x.DestinationStorageLocation.DisplayAddress,
                x.DestinationStorageLocation.Warehouse?.Name)).ToArray(), totalItems);
    }

    private static DateTimeOffset StartOfLocalDayUtc(DateTime date) =>
        new(TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(date.Date, DateTimeKind.Unspecified),
            TimeZoneInfo.Local), TimeSpan.Zero);

    public async Task<IReadOnlyList<ReceivingWorkOrderQueueView>> ListQueueAsync(
        Guid warehouseId, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        return await db.ReceivingOrders.AsNoTracking()
            .Where(x => x.WarehouseId == warehouseId
                && x.Status != ReceivingOrderStatus.Received)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => new ReceivingWorkOrderQueueView(
                x.Id, x.Number, x.CreatedAtUtc, x.WarehouseId, x.Status))
            .ToArrayAsync(ct);
    }

    public async Task<IReadOnlyList<ReceivingWorkOrderListView>> ListOrdersAsync(
        Guid? warehouseId, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var orders = await db.ReceivingOrders.AsNoTracking()
            .Where(x => warehouseId == null || x.WarehouseId == warehouseId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => new
            {
                x.Id, x.Number, x.CreatedAtUtc, x.WarehouseId, x.Status,
                WarehouseName = x.Warehouse == null ? null : x.Warehouse.Name
            })
            .ToListAsync(ct);
        if (orders.Count == 0)
            return [];

        var sourceCounts = await db.ReceivingOrderSourceLinks.AsNoTracking()
            .Where(x => x.DetachedAtUtc == null
                && x.ReceivingSourceDocument != null
                && (warehouseId == null || x.ReceivingOrder!.WarehouseId == warehouseId))
            .GroupBy(x => x.ReceivingOrderId)
            .Select(x => new { OrderId = x.Key, Count = x.Count() })
            .ToDictionaryAsync(x => x.OrderId, x => x.Count, ct);

        var items = await db.ReceivingOrderItems.AsNoTracking()
            .Where(x => warehouseId == null || x.ReceivingOrder!.WarehouseId == warehouseId)
            .Select(x => new
            {
                x.ReceivingOrderId, x.StockKeepingUnitId,
                x.CurrentPlanQuantity, x.FactQuantity
            })
            .ToArrayAsync(ct);
        var itemsByOrder = items.ToLookup(x => x.ReceivingOrderId);

        var postedSkus = await db.LicensePlateNumbers.AsNoTracking()
            .Where(x => x.ReceivingOrderId != null
                && x.Status == LicensePlateNumberStatus.Closed
                && (warehouseId == null || x.ReceivingOrder!.WarehouseId == warehouseId))
            .SelectMany(x => x.Items.Where(item => item.DeletedAtUtc == null),
                (lpn, item) => new { lpn.ReceivingOrderId, item.StockKeepingUnitId })
            .Distinct().ToArrayAsync(ct);
        var postedSkusByOrder = postedSkus.ToLookup(
            x => x.ReceivingOrderId!.Value, x => x.StockKeepingUnitId);

        var latestCycles = await db.ReceivingCycles.AsNoTracking()
            .Where(x => x.CompletedAtUtc != null
                && x.ReceivingOrder!.Status == ReceivingOrderStatus.Received
                && (warehouseId == null || x.ReceivingOrder!.WarehouseId == warehouseId)
                && !db.ReceivingCycles.Any(newer =>
                    newer.ReceivingOrderId == x.ReceivingOrderId
                    && newer.CompletedAtUtc != null && newer.Number > x.Number))
            .Select(x => new
            {
                x.ReceivingOrderId, x.PlanSnapshotJson, x.FactSnapshotJson
            })
            .ToDictionaryAsync(x => x.ReceivingOrderId, ct);

        var sourceSkus = await LoadCurrentSourceSkusAsync(db, null, warehouseId, ct);
        return orders.Select(order =>
        {
            var currentSkuIds = sourceSkus[order.Id].ToHashSet();
            currentSkuIds.UnionWith(postedSkusByOrder[order.Id]);
            var currentItems = itemsByOrder[order.Id]
                .Where(item => currentSkuIds.Contains(item.StockKeepingUnitId))
                .ToArray();
            var discrepancies = currentItems.Where(item =>
                item.CurrentPlanQuantity is null || item.FactQuantity is null
                || item.CurrentPlanQuantity != item.FactQuantity).ToArray();

            var hasNewDiscrepancy = false;
            if (discrepancies.Length > 0
                && order.Status == ReceivingOrderStatus.Received
                && latestCycles.TryGetValue(order.Id, out var latestCycle))
            {
                var oldPlans = ReadSnapshot(latestCycle.PlanSnapshotJson);
                var oldFacts = ReadSnapshot(latestCycle.FactSnapshotJson);
                hasNewDiscrepancy = discrepancies.Any(item =>
                    !oldPlans.ContainsKey(item.StockKeepingUnitId)
                        && !oldFacts.ContainsKey(item.StockKeepingUnitId)
                    || oldPlans.GetValueOrDefault(item.StockKeepingUnitId)
                        != item.CurrentPlanQuantity
                    || oldFacts.GetValueOrDefault(item.StockKeepingUnitId)
                        != item.FactQuantity);
            }

            return new ReceivingWorkOrderListView(order.Id, order.Number,
                order.CreatedAtUtc, order.WarehouseId, order.WarehouseName,
                order.Status, sourceCounts.GetValueOrDefault(order.Id),
                currentItems.Length, discrepancies.Length, hasNewDiscrepancy);
        }).ToArray();
    }

    public async Task<ReceivingWorkOrderView?> GetOrderAsync(
        Guid orderId, string userId, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var order = await OrderQuery(db)
            .SingleOrDefaultAsync(x => x.Id == orderId, ct);
        if (order is null)
            return null;
        var sourceSkus = await LoadCurrentSourceSkusAsync(db, orderId, null, ct);
        return MapOrder(order, userId, sourceSkus);
    }

    public async Task<ReceivingWorkOrderView?> GetMobileOrderAsync(
        Guid orderId, string userId, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var order = await OrderCoreQuery(db).AsSplitQuery()
            .SingleOrDefaultAsync(x => x.Id == orderId, ct);
        if (order is null)
            return null;
        var isParticipant = await db.ReceivingOrderParticipants.AsNoTracking()
            .AnyAsync(x => x.ReceivingOrderId == orderId && x.UserId == userId
                && x.ReceivingCycle!.Number == order.ActiveCycleNumber, ct);
        var sourceSkus = await LoadCurrentSourceSkusAsync(db, orderId, null, ct);
        return MapOrder(order, userId, sourceSkus, isParticipant, includeCycles: false);
    }

    public async Task<ReceivingOrderStatus?> GetOrderStatusAsync(
        Guid orderId, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        return await db.ReceivingOrders.AsNoTracking()
            .Where(x => x.Id == orderId)
            .Select(x => (ReceivingOrderStatus?)x.Status)
            .SingleOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<Guid>?> GetOrderSourceRefKeysAsync(
        Guid orderId, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        if (!await db.ReceivingOrders.AsNoTracking()
            .AnyAsync(x => x.Id == orderId, ct))
            return null;
        return await db.ReceivingOrderSourceLinks.AsNoTracking()
            .Where(x => x.ReceivingOrderId == orderId
                && x.DetachedAtUtc == null
                && x.ReceivingSourceDocument != null)
            .Select(x => x.ReceivingSourceDocument!.RefKey)
            .ToArrayAsync(ct);
    }

    public async Task<Guid?> FindLinkedOrderIdBySourceAsync(
        Guid refKey, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        return await db.ReceivingOrderSourceLinks.AsNoTracking()
            .Where(x => x.DetachedAtUtc == null
                && x.ReceivingSourceDocument != null
                && x.ReceivingSourceDocument.RefKey == refKey)
            .Select(x => (Guid?)x.ReceivingOrderId).SingleOrDefaultAsync(ct);
    }

    public async Task<ReceivingWorkLpnView?> GetLpnAsync(
        Guid? lpnId, string? code, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var query = db.LicensePlateNumbers.AsNoTracking()
            .Include(x => x.ReceivingOrder)
            .Include(x => x.Items).ThenInclude(x => x.StockKeepingUnit)
                .ThenInclude(x => x!.BaseUnitOfMeasure)
            .Include(x => x.Putaways).ThenInclude(x => x.DestinationStorageLocation)
            .AsSplitQuery();
        var lpn = lpnId is Guid id
            ? await query.SingleOrDefaultAsync(x => x.Id == id, ct)
            : await query.SingleOrDefaultAsync(x => x.Code == code, ct);
        return lpn is null || lpn.ReceivingCycleId is null
            || lpn.ReceivingOrder is null ? null
            : MapLpn(lpn, lpn.ReceivingOrder.WarehouseId);
    }

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<ReceivingLpnReversalHistoryView>>>
        GetDeletionHistoryAsync(Guid orderId, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var reversals = await db.InventoryMovements.AsNoTracking()
            .Include(x => x.SourceStorageLocation)
            .Include(x => x.DestinationStorageLocation)
            .Where(x => x.RecorderType == RecorderType.ReceivingOrder
                && x.RecorderId == orderId && x.LicensePlateNumberId != null
                && x.ReversedMovementId != null)
            .OrderBy(x => x.PostedAtUtc).ThenBy(x => x.Id)
            .ToArrayAsync(ct);
        return reversals.GroupBy(x => x.LicensePlateNumberId!.Value)
            .ToDictionary(x => x.Key, x => (IReadOnlyList<ReceivingLpnReversalHistoryView>)
                x.Select(movement => new ReceivingLpnReversalHistoryView(
                    movement.LicensePlateNumberId!.Value, movement.Id,
                    movement.ReversedMovementId!.Value,
                    movement.SourceStorageLocation?.DisplayAddress,
                    movement.DestinationStorageLocation?.DisplayAddress,
                    movement.StockKeepingUnitId, movement.ExpirationDate,
                    movement.Quantity, movement.PostedAtUtc,
                    movement.ConfirmedBy)).ToArray());
    }

    public async Task<IReadOnlyList<ReceivingSourceChangeHistoryView>>
        GetSourceChangeHistoryAsync(Guid orderId, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var events = await (from audit in db.WarehouseAuditEvents.AsNoTracking()
            join source in db.ReceivingSourceDocuments.AsNoTracking()
                on audit.ReceivingSourceDocumentId equals (Guid?)source.Id
            where audit.ReceivingOrderId == orderId
                && (audit.EventType == "receiving.order.source-attached"
                    || audit.EventType == "receiving.order.source-detached")
            orderby audit.OccurredAtUtc descending, audit.Id descending
            select new
            {
                source.RefKey, audit.EventType, audit.OccurredAtUtc,
                Actor = db.Users.Where(user => user.Id == audit.ActorUserId)
                    .Select(user => user.DisplayName).FirstOrDefault()
            }).ToArrayAsync(ct);
        return events.Select(x => new ReceivingSourceChangeHistoryView(
            x.RefKey, x.EventType == "receiving.order.source-detached",
            x.OccurredAtUtc, string.IsNullOrWhiteSpace(x.Actor) ? "Имя не указано" : x.Actor)).ToArray();
    }

    public async Task<IReadOnlyList<ReceivingPutawayRecommendationView>>
        GetPutawayRecommendationsAsync(
            Guid warehouseId, Guid skuId, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var locations = await db.StorageLocations.AsNoTracking()
            .Where(x => x.WarehouseId == warehouseId
                && x.Warehouse != null && !x.Warehouse.DeletionMark
                && x.Zone != null && !x.Zone.DeletionMark
                && x.Zone.Type == ZoneType.Storage
                && !x.IsFolder && !x.DeletionMark && x.ActiveLock == null)
            .Select(x => new
            {
                x.Id, x.Code, x.Name, x.PickSequence,
                ZoneCode = x.Zone!.Code,
                CurrentSkuQuantity = db.InventoryBalances
                    .Where(b => b.StorageLocationId == x.Id
                        && b.StockKeepingUnitId == skuId)
                    .Sum(b => (decimal?)b.Quantity) ?? 0m,
                HasSelectedSkuBalance = db.InventoryBalances.Any(b =>
                    b.StorageLocationId == x.Id
                    && b.StockKeepingUnitId == skuId),
                HasAnyNonzeroBalance = db.InventoryBalances.Any(b =>
                    b.StorageLocationId == x.Id && b.Quantity != 0)
            })
            .Where(x => x.CurrentSkuQuantity > 0
                || x.CurrentSkuQuantity == 0 && !x.HasAnyNonzeroBalance
                    && x.HasSelectedSkuBalance)
            .OrderByDescending(x => x.CurrentSkuQuantity > 0)
            .ThenBy(x => x.PickSequence.HasValue ? 0 : 1)
            .ThenBy(x => x.PickSequence)
            .ThenBy(x => x.ZoneCode).ThenBy(x => x.Code).ThenBy(x => x.Id)
            .ToListAsync(ct);
        return locations.Select(x => new ReceivingPutawayRecommendationView(
            x.Id, x.ZoneCode + "-" + x.Code, x.Name,
            x.CurrentSkuQuantity > 0 ? x.CurrentSkuQuantity : null)).ToArray();
    }

    private static IQueryable<ReceivingOrder> OrderCoreQuery(ApplicationDbContext db) =>
        db.ReceivingOrders.AsNoTracking()
            .Include(x => x.Warehouse)
            .Include(x => x.ReceivingLocation)
            .Include(x => x.Items).ThenInclude(x => x.StockKeepingUnit)
                .ThenInclude(x => x!.BaseUnitOfMeasure)
            .Include(x => x.SourceLinks).ThenInclude(x => x.ReceivingSourceDocument)
            .Include(x => x.LicensePlateNumbers).ThenInclude(x => x.Items)
                .ThenInclude(x => x.StockKeepingUnit)
                    .ThenInclude(x => x.BaseUnitOfMeasure)
            .Include(x => x.LicensePlateNumbers).ThenInclude(x => x.Putaways);

    private static IQueryable<ReceivingOrder> OrderQuery(ApplicationDbContext db) =>
        OrderCoreQuery(db)
            .Include(x => x.Cycles).ThenInclude(x => x.ReceivingLocation)
            .Include(x => x.Participants)
            .Include(x => x.Cycles).ThenInclude(x => x.SourceVersions)
                .ThenInclude(x => x.ReceivingSourceDocument)
            .Include(x => x.LicensePlateNumbers).ThenInclude(x => x.Putaways)
                .ThenInclude(x => x.DestinationStorageLocation)
            .AsSplitQuery();

    private static async Task<ILookup<Guid, Guid>> LoadCurrentSourceSkusAsync(
        ApplicationDbContext db, Guid? orderId, Guid? warehouseId, CancellationToken ct)
    {
        var rows = await (from link in db.ReceivingOrderSourceLinks.AsNoTracking()
            join order in db.ReceivingOrders.AsNoTracking()
                on link.ReceivingOrderId equals order.Id
            join source in db.ReceivingSourceDocuments.AsNoTracking()
                on link.ReceivingSourceDocumentId equals source.Id
            join line in db.ReceivingSourceLineProjections.AsNoTracking()
                on source.LastSuccessfulVersionId equals (Guid?)line.ReceivingSourceVersionId
            where link.DetachedAtUtc == null
                && (orderId == null || link.ReceivingOrderId == orderId)
                && (warehouseId == null || order.WarehouseId == warehouseId)
                && line.StockKeepingUnitId != null
            select new { link.ReceivingOrderId, SkuId = line.StockKeepingUnitId!.Value })
            .Distinct().ToArrayAsync(ct);
        return rows.ToLookup(x => x.ReceivingOrderId, x => x.SkuId);
    }

    private static ReceivingWorkOrderView MapOrder(
        ReceivingOrder order, string userId, ILookup<Guid, Guid> sourceSkus,
        bool? isParticipantOverride = null, bool includeCycles = true)
    {
        var activeCycleId = includeCycles ? order.Cycles
            .SingleOrDefault(x => x.Number == order.ActiveCycleNumber)?.Id : null;
        var currentSkuIds = sourceSkus[order.Id].ToHashSet();
        currentSkuIds.UnionWith(ReceivingCurrentComposition.PostedSkuIds(order));
        return new ReceivingWorkOrderView(
            order.Id, order.Number, order.CreatedAtUtc,
            order.WarehouseId, order.Status,
            order.ActiveCycleNumber, order.ReceivingLocationId,
            isParticipantOverride ?? (activeCycleId is Guid cycleId
                && order.Participants.Any(x =>
                    x.ReceivingCycleId == cycleId && x.UserId == userId)),
            order.OperationalRevision,
            order.SourceLinks.Where(x => x.DetachedAtUtc is null
                    && x.ReceivingSourceDocument is not null)
                .Select(x => x.ReceivingSourceDocument!.RefKey).ToArray(),
            order.Items.OrderBy(x => x.StockKeepingUnitId).Select(x =>
                new ReceivingWorkItemView(x.StockKeepingUnitId,
                    x.StockKeepingUnit?.Code, x.StockKeepingUnit?.Name,
                    x.StockKeepingUnit?.TracksExpirationDate == true,
                    x.StockKeepingUnit?.BaseUnitOfMeasure?.Description
                        ?? x.StockKeepingUnit?.BaseUnitOfMeasure?.Abbreviation,
                    x.CurrentPlanQuantity, x.FactQuantity)
                { IsCurrent = currentSkuIds.Contains(x.StockKeepingUnitId) }).ToArray(),
            order.LicensePlateNumbers.Where(x => x.ReceivingCycleId is not null)
                .OrderBy(x => x.Code, StringComparer.OrdinalIgnoreCase)
                .Select(x => MapLpn(x, order.WarehouseId)).ToArray(),
            includeCycles ? order.Cycles.OrderByDescending(x => x.Number)
                .Select(MapCycle).ToArray() : [])
        {
            WarehouseName = order.Warehouse?.Name,
            ReceivingLocationAddress = order.ReceivingLocation?.DisplayAddress
        };
    }

    private sealed record SnapshotQuantity(Guid SkuId, decimal? Quantity);

    private static ReceivingWorkCycleView MapCycle(ReceivingCycle cycle)
    {
        var plans = ReadSnapshot(cycle.PlanSnapshotJson);
        var facts = ReadSnapshot(cycle.FactSnapshotJson);
        var items = plans.Keys.Union(facts.Keys).Order().Select(skuId =>
            new ReceivingWorkCycleItemView(skuId,
                plans.GetValueOrDefault(skuId), facts.GetValueOrDefault(skuId))).ToArray();
        return new ReceivingWorkCycleView(cycle.Number, cycle.ReceivingLocationId,
            cycle.StartedAtUtc, cycle.CompletedAtUtc, cycle.CompletedBy,
            cycle.SourceCheckFailed, items,
            cycle.SourceVersions.Select(x => new ReceivingWorkCycleSourceView(
                x.ReceivingSourceDocument?.RefKey ?? Guid.Empty,
                x.ReceivingSourceVersionId)).ToArray())
        {
            ReceivingLocationAddress = cycle.ReceivingLocation?.DisplayAddress
        };
    }

    private static IReadOnlyDictionary<Guid, decimal?> ReadSnapshot(string? json) =>
        string.IsNullOrWhiteSpace(json)
            ? new Dictionary<Guid, decimal?>()
            : (JsonSerializer.Deserialize<SnapshotQuantity[]>(json) ?? [])
                .ToDictionary(x => x.SkuId, x => x.Quantity);

    private static ReceivingWorkLpnView MapLpn(
        LicensePlateNumber lpn, Guid warehouseId)
    {
        var placedByItem = lpn.Putaways.GroupBy(x => x.LicensePlateNumberItemId)
            .ToDictionary(x => x.Key, x => x.Sum(y => y.Quantity));
        return new ReceivingWorkLpnView(
            lpn.Id, lpn.Code, lpn.ReceivingOrderId!.Value, warehouseId,
            lpn.Status, lpn.ResponsibleUserId,
            lpn.OriginalReceivingLocationId, lpn.OperationalRevision,
            lpn.Items.Where(x => !x.IsDeleted).Select(x =>
                new ReceivingWorkLpnItemView(x.Id, x.StockKeepingUnitId,
                    x.StockKeepingUnit?.Code, x.StockKeepingUnit?.Name,
                    x.StockKeepingUnit?.BaseUnitOfMeasure?.Description
                        ?? x.StockKeepingUnit?.BaseUnitOfMeasure?.Abbreviation,
                    x.Quantity, x.ExpirationDate,
                    placedByItem.GetValueOrDefault(x.Id))).ToArray(),
            lpn.Putaways.OrderByDescending(x => x.CreatedAtUtc).Select(x =>
                new ReceivingWorkPutawayView(x.Id, x.LicensePlateNumberItemId,
                    x.DestinationStorageLocationId, x.Quantity,
                    x.CreatedAtUtc, x.InventoryMovementId)
                {
                    DestinationAddress = x.DestinationStorageLocation?.DisplayAddress,
                    CreatedBy = x.CreatedBy
                }).ToArray());
    }
}

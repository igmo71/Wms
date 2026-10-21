using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Wms.Application.Receiving;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Data;

public sealed class ReceivingWorkQueryStore(
    IDbContextFactory<ApplicationDbContext> contextFactory) : IReceivingWorkQueryStore
{
    public async Task<IReadOnlyList<ReceivingWorkOrderView>> ListOrdersAsync(
        Guid? warehouseId, string userId, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var orders = await OrderQuery(db)
            .Where(x => warehouseId == null || x.WarehouseId == warehouseId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(ct);
        var sourceSkus = await LoadCurrentSourceSkusAsync(db, null, warehouseId, ct);
        return orders.Select(x => MapOrder(x, userId, sourceSkus)).ToArray();
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

    public async Task<ReceivingWorkOrderView?> GetOrderBySourceAsync(
        Guid refKey, string userId, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var orderId = await db.ReceivingOrderSourceLinks.AsNoTracking()
            .Where(x => x.DetachedAtUtc == null
                && x.ReceivingSourceDocument != null
                && x.ReceivingSourceDocument.RefKey == refKey)
            .Select(x => (Guid?)x.ReceivingOrderId).SingleOrDefaultAsync(ct);
        return orderId is Guid id ? await GetOrderAsync(id, userId, ct) : null;
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

    private static IQueryable<ReceivingOrder> OrderQuery(ApplicationDbContext db) =>
        db.ReceivingOrders.AsNoTracking()
            .Include(x => x.Warehouse)
            .Include(x => x.ReceivingLocation)
            .Include(x => x.Cycles).ThenInclude(x => x.ReceivingLocation)
            .Include(x => x.Items).ThenInclude(x => x.StockKeepingUnit)
                .ThenInclude(x => x!.BaseUnitOfMeasure)
            .Include(x => x.Participants)
            .Include(x => x.Cycles).ThenInclude(x => x.SourceVersions)
                .ThenInclude(x => x.ReceivingSourceDocument)
            .Include(x => x.SourceLinks).ThenInclude(x => x.ReceivingSourceDocument)
            .Include(x => x.LicensePlateNumbers).ThenInclude(x => x.Items)
                .ThenInclude(x => x.StockKeepingUnit)
                    .ThenInclude(x => x.BaseUnitOfMeasure)
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
        ReceivingOrder order, string userId, ILookup<Guid, Guid> sourceSkus)
    {
        var activeCycleId = order.Cycles
            .SingleOrDefault(x => x.Number == order.ActiveCycleNumber)?.Id;
        var currentSkuIds = sourceSkus[order.Id].ToHashSet();
        currentSkuIds.UnionWith(ReceivingCurrentComposition.PostedSkuIds(order));
        return new ReceivingWorkOrderView(
            order.Id, order.Number, order.CreatedAtUtc,
            order.WarehouseId, order.Status,
            order.ActiveCycleNumber, order.ReceivingLocationId,
            activeCycleId is Guid cycleId && order.Participants.Any(x =>
                x.ReceivingCycleId == cycleId && x.UserId == userId),
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
            order.Cycles.OrderByDescending(x => x.Number)
                .Select(MapCycle).ToArray())
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

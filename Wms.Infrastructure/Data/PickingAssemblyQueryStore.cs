using Microsoft.EntityFrameworkCore;
using Wms.Application.Picking;
using Wms.Domain.Enums;

namespace Wms.Data;

public sealed class PickingAssemblyQueryStore(
    IDbContextFactory<ApplicationDbContext> contextFactory) : IPickingAssemblyQueryStore
{
    public async Task<IReadOnlyList<PickingAssemblyListItem>> ListForOperatorAsync(
        Guid warehouseId, string userId, CancellationToken ct)
        => await ListAsync(warehouseId, userId, ct);

    public Task<IReadOnlyList<PickingAssemblyListItem>> ListForManagerAsync(CancellationToken ct)
        => ListAsync(null, null, ct);

    private async Task<IReadOnlyList<PickingAssemblyListItem>> ListAsync(
        Guid? warehouseId, string? userId, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var assemblies = await db.PickingAssemblies.AsNoTracking()
            .Where(x => warehouseId == null || x.WarehouseId == warehouseId)
            .Where(x => userId == null || x.Status != PickingAssemblyStatus.Cancelled
                && (x.CreatedBy == userId || x.ResponsibleUserId == userId
                    || x.Status == PickingAssemblyStatus.Closed))
            .Include(x => x.SourceLinks).ThenInclude(x => x.ShippingOrder)
            .OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id)
            .AsSplitQuery().ToArrayAsync(ct);
        if (assemblies.Length == 0)
            return [];
        var assemblyIds = assemblies.Select(x => x.Id).ToArray();
        var targets = await db.ShippingExchangeTargets.AsNoTracking()
            .Where(x => assemblyIds.Contains(x.PickingAssemblyId))
            .ToArrayAsync(ct);
        var exchange = targets.ToDictionary(x => (x.PickingAssemblyId, x.ShippingOrderId));
        var ids = assemblies.Select(x => x.ResponsibleUserId).Distinct().ToArray();
        var users = await db.Users.AsNoTracking().Where(x => ids.Contains(x.Id))
            .Select(x => new { x.Id, x.DisplayName, x.UserName }).ToArrayAsync(ct);
        var names = users.ToDictionary(x => x.Id,
            x => DisplayName(x.DisplayName, x.UserName, x.Id));
        var warehouseIds = assemblies.Select(x => x.WarehouseId).Distinct().ToArray();
        var warehouseNames = await db.Warehouses.AsNoTracking()
            .Where(x => warehouseIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, ct);

        return assemblies.Where(x => userId is null
            || x.Status != PickingAssemblyStatus.Closed
            || x.ActiveSources.Any(link => !exchange.TryGetValue(
                (x.Id, link.ShippingOrderId), out var target) || !target.IsConfirmed))
            .Select(x => new PickingAssemblyListItem(
            x.Id, x.Number, x.WarehouseId, x.Status,
            x.OperationalRevision, x.CreatedBy, x.ResponsibleUserId,
            names.GetValueOrDefault(x.ResponsibleUserId, x.ResponsibleUserId),
            x.CreatedAtUtc, x.StartedAtUtc,
            x.ActiveSources.Select(link => new PickingAssemblyListSource(
                link.ShippingOrderId, link.ShippingOrder?.Number,
                link.StartConfirmedAtUtc is not null, link.LastStartError,
                link.ShippingOrder?.LastSourceCheckError)
                {
                    ExchangeStage = exchange.GetValueOrDefault(
                        (x.Id, link.ShippingOrderId))?.Stage,
                    ExchangeError = exchange.GetValueOrDefault(
                        (x.Id, link.ShippingOrderId))?.LastError
                }).ToArray())
            { WarehouseName = warehouseNames.GetValueOrDefault(x.WarehouseId) })
            .ToArray();
    }

    public async Task<PickingAssemblyView?> GetAsync(Guid assemblyId, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var assembly = await db.PickingAssemblies.AsNoTracking()
            .Include(x => x.SourceLinks).ThenInclude(x => x.ShippingOrder)
            .Include(x => x.PlanLines).ThenInclude(x => x.StockKeepingUnit)
            .AsSplitQuery().SingleOrDefaultAsync(x => x.Id == assemblyId, ct);
        if (assembly is null)
            return null;
        var owner = await db.Users.AsNoTracking()
            .Where(x => x.Id == assembly.ResponsibleUserId)
            .Select(x => new { x.DisplayName, x.UserName })
            .SingleOrDefaultAsync(ct);
        var ownerName = DisplayName(owner?.DisplayName, owner?.UserName,
            assembly.ResponsibleUserId);
        var warehouseName = await db.Warehouses.AsNoTracking()
            .Where(x => x.Id == assembly.WarehouseId)
            .Select(x => x.Name).SingleOrDefaultAsync(ct);
        var sourceIds = assembly.ActiveSources.Select(x => x.ShippingOrderId).ToArray();
        var targets = await db.ShippingExchangeTargets.AsNoTracking()
            .Where(x => x.PickingAssemblyId == assemblyId)
            .ToDictionaryAsync(x => x.ShippingOrderId, ct);
        var currentLines = await db.ShippingOrderItems.AsNoTracking()
            .Where(x => sourceIds.Contains(x.ShippingOrderId))
            .Include(x => x.StockKeepingUnit)
            .OrderBy(x => x.LineNumber)
            .Select(x => new { x.ShippingOrderId, Line = new PickingSourceLineView(
                x.LineNumber, x.StockKeepingUnitId, x.StockKeepingUnit!.Code,
                x.StockKeepingUnit.Name, x.PlanQuantity) })
            .ToArrayAsync(ct);

        return new PickingAssemblyView(
            assembly.Id, assembly.Number, assembly.WarehouseId, assembly.Status,
            assembly.OperationalRevision, assembly.CreatedBy,
            assembly.ResponsibleUserId, ownerName, assembly.CreatedAtUtc,
            assembly.FirstStartAttemptAtUtc, assembly.StartedAtUtc, assembly.ClosedAtUtc,
            assembly.ActiveSources.Select(link =>
            {
                var source = link.ShippingOrder;
                return new PickingAssemblySourceView(
                    link.ShippingOrderId, source?.Number,
                    source?.Status ?? ShippingOrderStatus.Unknown,
                    source?.Posted ?? false, source?.DeletionMark ?? false,
                    source?.LastSourceCheckAtUtc, source?.LastSourceCheckError,
                    link.StartConfirmedAtUtc, link.LastStartAttemptAtUtc,
                    link.LastStartError, link.PlanSourceSnapshotJson,
                    source?.CurrentSourceJson)
                {
                    WarehouseId = source?.WarehouseId,
                    WarehouseName = warehouseName,
                    ReceiverId = source?.ReceiverId,
                    ReceiverName = source?.Receiver?.Name,
                    Queue = source?.Queue.ToString(),
                    PlannedShippingDate = source?.PlannedShippingDate,
                    ExchangeStage = targets.GetValueOrDefault(link.ShippingOrderId)?.Stage,
                    ExchangeError = targets.GetValueOrDefault(link.ShippingOrderId)?.LastError,
                    ExchangeRevision = targets.GetValueOrDefault(link.ShippingOrderId)?.OperationalRevision,
                    CurrentLines = currentLines.Where(x => x.ShippingOrderId == link.ShippingOrderId)
                        .Select(x => x.Line).ToArray()
                };
            }).ToArray(),
            assembly.PlanLines.Select(line => new PickingAssemblyPlanLineView(
                line.ShippingOrderId, line.StockKeepingUnitId,
                line.StockKeepingUnit?.Code, line.StockKeepingUnit?.Name,
                line.Quantity)).ToArray()) { WarehouseName = warehouseName };
    }

    public async Task<PickingWorkView?> GetWorkAsync(Guid assemblyId, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var assembly = await db.PickingAssemblies.AsNoTracking()
            .Include(x => x.PlanLines)
            .SingleOrDefaultAsync(x => x.Id == assemblyId, ct);
        if (assembly is null) return null;
        var entries = await db.PickingEntries.AsNoTracking()
            .Where(x => x.PickingAssemblyId == assemblyId && x.Quantity > 0)
            .Join(db.StorageLocations.AsNoTracking(), entry => entry.StorageLocationId,
                location => location.Id,
                (entry, location) => new PickingWorkEntryView(entry.Id,
                    entry.StorageLocationId, location.Code, entry.StockKeepingUnitId,
                    entry.ExpirationDate, entry.Quantity, entry.ConfirmedBy,
                    entry.ConfirmedAtUtc, location.Zone!.Type == ZoneType.Reserve))
            .ToArrayAsync(ct);
        var picked = entries.GroupBy(x => x.SkuId)
            .ToDictionary(x => x.Key, x => x.Sum(row => row.Quantity));
        var planned = assembly.PlanLines.GroupBy(x => x.StockKeepingUnitId)
            .ToDictionary(x => x.Key, x => x.Sum(row => row.Quantity));
        var skuIds = planned.Keys.Union(picked.Keys).ToArray();
        var knownSkus = await db.StockKeepingUnits.AsNoTracking()
            .Include(x => x.BaseUnitOfMeasure)
            .Where(x => skuIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);
        var skus = skuIds.Select(skuId =>
            {
                knownSkus.TryGetValue(skuId, out var sku);
                return new PickingWorkSkuView(skuId, sku?.Code, sku?.Name,
                    sku?.BaseUnitOfMeasure?.Abbreviation
                        ?? sku?.BaseUnitOfMeasure?.Name,
                    sku?.TracksExpirationDate ?? false,
                    planned.GetValueOrDefault(skuId), picked.GetValueOrDefault(skuId));
            }).OrderBy(x => x.Name).ThenBy(x => x.Code).ThenBy(x => x.SkuId)
            .ToArray();
        return new PickingWorkView(assembly.Id, assembly.WarehouseId,
            assembly.Status, assembly.OperationalRevision,
            assembly.ResponsibleUserId, skus, entries);
    }

    public async Task<IReadOnlyList<PickingAuditView>> GetAuditAsync(
        Guid assemblyId, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        return await db.WarehouseAuditEvents.AsNoTracking()
            .Where(x => x.PickingAssemblyId == assemblyId)
            .OrderByDescending(x => x.OccurredAtUtc)
            .Select(x => new PickingAuditView(x.OccurredAtUtc, x.EventType,
                x.ActorUserId, x.BeforeValuesJson, x.AfterValuesJson))
            .ToArrayAsync(ct);
    }

    public async Task<IReadOnlyList<PickingAllocationView>> GetPostedAllocationsAsync(
        Guid assemblyId, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        return await db.PickingAllocations.AsNoTracking()
            .Where(x => x.PickingAssemblyId == assemblyId)
            .OrderBy(x => x.ShippingOrderId).ThenBy(x => x.StockKeepingUnitId)
            .Select(x => new PickingAllocationView(x.ShippingOrderId,
                x.StockKeepingUnitId, x.Quantity)).ToArrayAsync(ct);
    }

    public async Task<IReadOnlyList<PickingShortageView>> GetShortagesAsync(
        Guid assemblyId, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var shortages = await db.StockShortages.AsNoTracking()
            .Where(x => x.Origin == StockShortageOrigin.Picking && x.OriginId == assemblyId)
            .OrderByDescending(x => x.ReportedAtUtc).ToArrayAsync(ct);
        if (shortages.Length == 0) return [];
        var locationIds = shortages.Select(x => x.StorageLocationId).Distinct().ToArray();
        var skuIds = shortages.Select(x => x.StockKeepingUnitId).Distinct().ToArray();
        var locations = await db.StorageLocations.AsNoTracking()
            .Where(x => locationIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Code, ct);
        var skus = await db.StockKeepingUnits.AsNoTracking()
            .Where(x => skuIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name ?? x.Code, ct);
        var reservations = await db.PickingReservations.AsNoTracking()
            .Where(x => x.PickingAssemblyId == assemblyId
                && x.CompletedAtUtc == null && x.Quantity > 0)
            .Select(x => new { x.StorageLocationId, x.StockKeepingUnitId,
                x.ExpirationDate, x.Quantity }).ToArrayAsync(ct);
        return shortages.Select(x => new PickingShortageView(x.Id,
            x.StockKeepingUnitId, skus.GetValueOrDefault(x.StockKeepingUnitId),
            locations.GetValueOrDefault(x.StorageLocationId, x.StorageLocationId.ToString()),
            x.ExpirationDate, x.ExpectedQuantity, x.FoundQuantity,
            reservations.Where(r => r.StorageLocationId == x.StorageLocationId
                && r.StockKeepingUnitId == x.StockKeepingUnitId
                && r.ExpirationDate == x.ExpirationDate).Sum(r => r.Quantity),
            x.BlockedQuantity, x.Comment, x.ReportedBy, x.ReportedAtUtc,
            x.ResolvedAtUtc, x.ResolvedBy, x.ResolutionReason,
            x.CorrectionMovementId)).ToArray();
    }

    public async Task<IReadOnlyList<PickingRouteLocation>> GetRouteLocationsAsync(
        Guid warehouseId, IReadOnlyCollection<Guid> locationIds, CancellationToken ct)
    {
        var ids = locationIds.Distinct().ToArray();
        if (ids.Length == 0) return [];
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var counts = await db.InventoryBalances.AsNoTracking()
            .Where(x => x.WarehouseId == warehouseId
                && ids.Contains(x.StorageLocationId) && x.Quantity > 0)
            .GroupBy(x => x.StorageLocationId)
            .Select(group => new { LocationId = group.Key,
                Count = group.Select(x => x.StockKeepingUnitId).Distinct().Count() })
            .ToDictionaryAsync(x => x.LocationId, x => x.Count, ct);
        var locations = await db.StorageLocations.AsNoTracking()
            .Include(x => x.Zone).Include(x => x.Warehouse)
            .Where(x => x.WarehouseId == warehouseId && ids.Contains(x.Id))
            .ToArrayAsync(ct);
        return locations.Select(x => new PickingRouteLocation(x.Id, x.Code,
            x.Zone?.Type ?? ZoneType.Receiving,
            !x.IsFolder && !x.DeletionMark && x.Zone is { DeletionMark: false }
                && x.Warehouse is { DeletionMark: false },
            counts.GetValueOrDefault(x.Id))).ToArray();
    }

    private static string DisplayName(string? displayName, string? userName, string id) =>
        !string.IsNullOrWhiteSpace(displayName) ? displayName
        : !string.IsNullOrWhiteSpace(userName) ? userName : id;
}

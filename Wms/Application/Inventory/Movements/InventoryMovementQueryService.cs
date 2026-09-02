using Microsoft.EntityFrameworkCore;
using Wms.Common;
using Wms.Data;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Application.Inventory.Movements;

public class InventoryMovementQueryService(IDbContextFactory<ApplicationDbContext> dbContextFactory)
{
    public async Task<ListResult<InventoryMovementListItem>> ListAsync(
        InventoryMovementListQuery listQuery,
        CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        IQueryable<InventoryMovement> query = dbContext.InventoryMovements
            .AsNoTracking()
            .Include(x => x.Warehouse)
            .Include(x => x.SourceStorageLocation)
                .ThenInclude(x => x!.Zone)
            .Include(x => x.DestinationStorageLocation)
                .ThenInclude(x => x!.Zone)
            .Include(x => x.StockKeepingUnit)
            .Where(x => x.PostedAtUtc != null);

        query = await ApplyFiltersAsync(query, listQuery, dbContext, ct);

        var totalItems = await query.CountAsync(ct);

        query = ApplySorting(query, listQuery.SortBy, listQuery.SortDescending);

        var items = await query
            .Skip(listQuery.Skip)
            .Take(listQuery.Take)
            .ToListAsync(ct);

        var receivingOrderIds = items
            .Where(x => x.RecorderType == Domain.Enums.RecorderType.ReceivingOrder && x.RecorderId is not null)
            .Select(x => x.RecorderId!.Value)
            .Distinct()
            .ToArray();
        var shippingOrderIds = items
            .Where(x => x.RecorderType == Domain.Enums.RecorderType.ShippingOrder && x.RecorderId is not null)
            .Select(x => x.RecorderId!.Value)
            .Distinct()
            .ToArray();
        var inventoryCountIds = items
            .Where(x => x.RecorderType == RecorderType.InventoryCount && x.RecorderId is not null)
            .Select(x => x.RecorderId!.Value)
            .Distinct()
            .ToArray();
        var transferIds = items
            .Where(x => x.RecorderType == RecorderType.InventoryTransfer && x.RecorderId is not null)
            .Select(x => x.RecorderId!.Value)
            .Distinct()
            .ToArray();

        var receivingOrders = await dbContext.ReceivingOrders
            .AsNoTracking()
            .Where(x => receivingOrderIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);
        var shippingOrders = await dbContext.ShippingOrders
            .AsNoTracking()
            .Where(x => shippingOrderIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);
        var inventoryCounts = await dbContext.InventoryCounts
            .AsNoTracking()
            .Where(x => inventoryCountIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);
        var transfers = await dbContext.InventoryTransfers
            .AsNoTracking()
            .Where(x => transferIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);

        return new ListResult<InventoryMovementListItem>
        {
            Items = items.Select(movement => new InventoryMovementListItem
            {
                Movement = movement,
                RecorderNumber = movement.RecorderType switch
                {
                    Domain.Enums.RecorderType.ReceivingOrder when movement.RecorderId is Guid recorderId
                        && receivingOrders.TryGetValue(recorderId, out var order) => order.Number,
                    Domain.Enums.RecorderType.ShippingOrder when movement.RecorderId is Guid recorderId
                        && shippingOrders.TryGetValue(recorderId, out var order) => order.Number,
                    RecorderType.InventoryCount when movement.RecorderId is Guid recorderId
                        && inventoryCounts.TryGetValue(recorderId, out var count) => count.Number,
                    RecorderType.InventoryTransfer when movement.RecorderId is Guid recorderId
                        && transfers.TryGetValue(recorderId, out var transfer) => transfer.Number,
                    _ => null
                },
                RecorderDate = movement.RecorderType switch
                {
                    Domain.Enums.RecorderType.ReceivingOrder when movement.RecorderId is Guid recorderId
                        && receivingOrders.TryGetValue(recorderId, out var order) => order.Date,
                    Domain.Enums.RecorderType.ShippingOrder when movement.RecorderId is Guid recorderId
                        && shippingOrders.TryGetValue(recorderId, out var order) => order.Date,
                    RecorderType.InventoryCount when movement.RecorderId is Guid recorderId
                        && inventoryCounts.TryGetValue(recorderId, out var count) => count.Date,
                    RecorderType.InventoryTransfer when movement.RecorderId is Guid recorderId
                        && transfers.TryGetValue(recorderId, out var transfer) => transfer.Date,
                    _ => null
                }
            }).ToList(),
            TotalItems = totalItems
        };
    }

    private static async Task<IQueryable<InventoryMovement>> ApplyFiltersAsync(
        IQueryable<InventoryMovement> query,
        InventoryMovementListQuery listQuery,
        ApplicationDbContext dbContext,
        CancellationToken ct)
    {
        if (listQuery.WarehouseId is Guid warehouseId)
            query = query.Where(x => x.WarehouseId == warehouseId);

        if (listQuery.StorageLocationId is Guid storageLocationId)
            query = query.Where(x => x.SourceStorageLocationId == storageLocationId
                || x.DestinationStorageLocationId == storageLocationId);

        if (listQuery.StockKeepingUnitId is Guid stockKeepingUnitId)
            query = query.Where(x => x.StockKeepingUnitId == stockKeepingUnitId);

        if (listQuery.DateFrom is DateTime dateFrom)
            query = query.Where(x => x.PostedAtUtc >= dateFrom);

        if (listQuery.DateTo is DateTime dateTo)
            query = query.Where(x => x.PostedAtUtc < dateTo.Date.AddDays(1));

        if (!string.IsNullOrWhiteSpace(listQuery.DocumentSearchString))
        {
            var searchString = listQuery.DocumentSearchString;
            var receivingOrderIds = await dbContext.ReceivingOrders
                .Where(x => x.Number!.Contains(searchString))
                .Select(x => x.Id)
                .ToArrayAsync(ct);
            var shippingOrderIds = await dbContext.ShippingOrders
                .Where(x => x.Number!.Contains(searchString))
                .Select(x => x.Id)
                .ToArrayAsync(ct);
            var inventoryCountIds = await dbContext.InventoryCounts
                .Where(x => x.Number.Contains(searchString))
                .Select(x => x.Id)
                .ToArrayAsync(ct);
            var transferIds = await dbContext.InventoryTransfers
                .Where(x => x.Number!.Contains(searchString))
                .Select(x => x.Id)
                .ToArrayAsync(ct);

            query = query.Where(x => x.RecorderId != null
                && ((x.RecorderType == RecorderType.ReceivingOrder
                        && receivingOrderIds.Contains(x.RecorderId.Value))
                    || (x.RecorderType == RecorderType.ShippingOrder
                        && shippingOrderIds.Contains(x.RecorderId.Value))
                    || (x.RecorderType == RecorderType.InventoryCount
                        && inventoryCountIds.Contains(x.RecorderId.Value))
                    || (x.RecorderType == RecorderType.InventoryTransfer
                        && transferIds.Contains(x.RecorderId.Value))));
        }

        return query;
    }

    private static IQueryable<InventoryMovement> ApplySorting(
        IQueryable<InventoryMovement> query,
        string? sortBy,
        bool sortDescending)
    {
        return sortBy switch
        {
            "Warehouse" or "Warehouse.Name" => sortDescending
                ? query.OrderByDescending(x => x.Warehouse!.Name)
                : query.OrderBy(x => x.Warehouse!.Name),
            "SourceStorageLocation" or "SourceStorageLocation.Name" => sortDescending
                ? query.OrderByDescending(x => x.SourceStorageLocation!.Name)
                : query.OrderBy(x => x.SourceStorageLocation!.Name),
            "SourceStorageLocation.Code" => sortDescending
                ? query.OrderByDescending(x => x.SourceStorageLocation!.Zone!.Code)
                    .ThenByDescending(x => x.SourceStorageLocation!.Code)
                : query.OrderBy(x => x.SourceStorageLocation!.Zone!.Code)
                    .ThenBy(x => x.SourceStorageLocation!.Code),
            "DestinationStorageLocation" or "DestinationStorageLocation.Name" => sortDescending
                ? query.OrderByDescending(x => x.DestinationStorageLocation!.Name)
                : query.OrderBy(x => x.DestinationStorageLocation!.Name),
            "DestinationStorageLocation.Code" => sortDescending
                ? query.OrderByDescending(x => x.DestinationStorageLocation!.Zone!.Code)
                    .ThenByDescending(x => x.DestinationStorageLocation!.Code)
                : query.OrderBy(x => x.DestinationStorageLocation!.Zone!.Code)
                    .ThenBy(x => x.DestinationStorageLocation!.Code),
            "StockKeepingUnit" or "StockKeepingUnit.Name" => sortDescending
                ? query.OrderByDescending(x => x.StockKeepingUnit!.Name)
                : query.OrderBy(x => x.StockKeepingUnit!.Name),
            "Quantity" => sortDescending
                ? query.OrderByDescending(x => x.Quantity)
                : query.OrderBy(x => x.Quantity),
            "PostedAtUtc" => sortDescending
                ? query.OrderByDescending(x => x.PostedAtUtc)
                : query.OrderBy(x => x.PostedAtUtc),
            _ => query.OrderByDescending(x => x.PostedAtUtc)
        };
    }
}

using Microsoft.EntityFrameworkCore;
using Wms.Common;
using Wms.Data;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Application.Inventory.Turnovers;

public class InventoryTurnoverQueryService(IDbContextFactory<ApplicationDbContext> dbContextFactory)
{
    public async Task<ListResult<InventoryTurnoverListItem>> ListAsync(
        InventoryTurnoverListQuery listQuery,
        CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        IQueryable<InventoryTurnover> query = dbContext.InventoryTurnovers
            .AsNoTracking()
            .Include(x => x.Warehouse)
            .Include(x => x.StorageLocation)
                .ThenInclude(x => x!.Zone)
            .Include(x => x.StockKeepingUnit)
            .Include(x => x.InventoryMovement);

        query = await ApplyFiltersAsync(query, listQuery, dbContext, ct);

        var totalItems = await query.CountAsync(ct);

        query = ApplySorting(query, listQuery.SortBy, listQuery.SortDescending);

        var items = await query
            .Skip(listQuery.Skip)
            .Take(listQuery.Take)
            .ToListAsync(ct);

        var receivingOrderIds = items
            .Where(x => x.InventoryMovement?.RecorderType == RecorderType.ReceivingOrder
                && x.InventoryMovement.RecorderId is not null)
            .Select(x => x.InventoryMovement!.RecorderId!.Value)
            .Distinct()
            .ToArray();
        var shippingOrderIds = items
            .Where(x => x.InventoryMovement?.RecorderType == RecorderType.ShippingOrder
                && x.InventoryMovement.RecorderId is not null)
            .Select(x => x.InventoryMovement!.RecorderId!.Value)
            .Distinct()
            .ToArray();
        var transferIds = items
            .Where(x => x.InventoryMovement?.RecorderType == RecorderType.InventoryTransfer
                && x.InventoryMovement.RecorderId is not null)
            .Select(x => x.InventoryMovement!.RecorderId!.Value)
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
        var transfers = await dbContext.InventoryTransfers
            .AsNoTracking()
            .Where(x => transferIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);

        return new ListResult<InventoryTurnoverListItem>
        {
            Items = items.Select(turnover => new InventoryTurnoverListItem
            {
                Turnover = turnover,
                RecorderNumber = turnover.InventoryMovement?.RecorderType switch
                {
                    RecorderType.ReceivingOrder when turnover.InventoryMovement.RecorderId is Guid recorderId
                        && receivingOrders.TryGetValue(recorderId, out var order) => order.Number,
                    RecorderType.ShippingOrder when turnover.InventoryMovement.RecorderId is Guid recorderId
                        && shippingOrders.TryGetValue(recorderId, out var order) => order.Number,
                    RecorderType.InventoryTransfer when turnover.InventoryMovement.RecorderId is Guid recorderId
                        && transfers.TryGetValue(recorderId, out var transfer) => transfer.Number,
                    _ => null
                },
                RecorderDate = turnover.InventoryMovement?.RecorderType switch
                {
                    RecorderType.ReceivingOrder when turnover.InventoryMovement.RecorderId is Guid recorderId
                        && receivingOrders.TryGetValue(recorderId, out var order) => order.Date,
                    RecorderType.ShippingOrder when turnover.InventoryMovement.RecorderId is Guid recorderId
                        && shippingOrders.TryGetValue(recorderId, out var order) => order.Date,
                    RecorderType.InventoryTransfer when turnover.InventoryMovement.RecorderId is Guid recorderId
                        && transfers.TryGetValue(recorderId, out var transfer) => transfer.Date,
                    _ => null
                }
            }).ToList(),
            TotalItems = totalItems
        };
    }

    private static async Task<IQueryable<InventoryTurnover>> ApplyFiltersAsync(
        IQueryable<InventoryTurnover> query,
        InventoryTurnoverListQuery listQuery,
        ApplicationDbContext dbContext,
        CancellationToken ct)
    {
        if (listQuery.WarehouseId is Guid warehouseId)
            query = query.Where(x => x.WarehouseId == warehouseId);

        if (listQuery.StorageLocationId is Guid storageLocationId)
            query = query.Where(x => x.StorageLocationId == storageLocationId);

        if (listQuery.StockKeepingUnitId is Guid stockKeepingUnitId)
            query = query.Where(x => x.StockKeepingUnitId == stockKeepingUnitId);

        if (listQuery.DateFrom is DateTime dateFrom)
            query = query.Where(x => x.CreatedAtUtc >= dateFrom);

        if (listQuery.DateTo is DateTime dateTo)
            query = query.Where(x => x.CreatedAtUtc < dateTo.Date.AddDays(1));

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
            var transferIds = await dbContext.InventoryTransfers
                .Where(x => x.Number!.Contains(searchString))
                .Select(x => x.Id)
                .ToArrayAsync(ct);

            query = query.Where(x => x.InventoryMovement!.RecorderId != null
                && ((x.InventoryMovement.RecorderType == RecorderType.ReceivingOrder
                        && receivingOrderIds.Contains(x.InventoryMovement.RecorderId.Value))
                    || (x.InventoryMovement.RecorderType == RecorderType.ShippingOrder
                        && shippingOrderIds.Contains(x.InventoryMovement.RecorderId.Value))
                    || (x.InventoryMovement.RecorderType == RecorderType.InventoryTransfer
                        && transferIds.Contains(x.InventoryMovement.RecorderId.Value))));
        }

        return query;
    }

    private static IQueryable<InventoryTurnover> ApplySorting(
        IQueryable<InventoryTurnover> query,
        string? sortBy,
        bool sortDescending)
    {
        return sortBy switch
        {
            "Warehouse" or "Warehouse.Name" => sortDescending
                ? query.OrderByDescending(x => x.Warehouse!.Name)
                : query.OrderBy(x => x.Warehouse!.Name),
            "StorageLocation" or "StorageLocation.Name" => sortDescending
                ? query.OrderByDescending(x => x.StorageLocation!.Name)
                : query.OrderBy(x => x.StorageLocation!.Name),
            "StorageLocation.Code" => sortDescending
                ? query.OrderByDescending(x => x.StorageLocation!.Zone!.Code)
                    .ThenByDescending(x => x.StorageLocation!.Code)
                : query.OrderBy(x => x.StorageLocation!.Zone!.Code)
                    .ThenBy(x => x.StorageLocation!.Code),
            "StockKeepingUnit" or "StockKeepingUnit.Name" => sortDescending
                ? query.OrderByDescending(x => x.StockKeepingUnit!.Name)
                : query.OrderBy(x => x.StockKeepingUnit!.Name),
            "QuantityDelta" => sortDescending
                ? query.OrderByDescending(x => x.QuantityDelta)
                : query.OrderBy(x => x.QuantityDelta),
            "BalanceBefore" => sortDescending
                ? query.OrderByDescending(x => x.BalanceBefore)
                : query.OrderBy(x => x.BalanceBefore),
            "BalanceAfter" => sortDescending
                ? query.OrderByDescending(x => x.BalanceAfter)
                : query.OrderBy(x => x.BalanceAfter),
            "CreatedAtUtc" => sortDescending
                ? query.OrderByDescending(x => x.CreatedAtUtc)
                : query.OrderBy(x => x.CreatedAtUtc),
            _ => query.OrderByDescending(x => x.CreatedAtUtc)
        };
    }
}

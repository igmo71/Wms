using Microsoft.EntityFrameworkCore;
using Wms.Common;
using Wms.Data;
using Wms.Domain;

namespace Wms.Application.Inventory.Balances;

public class InventoryBalanceQueryService(IDbContextFactory<ApplicationDbContext> dbContextFactory)
{
    public async Task<ListResult<InventoryBalance>> ListAsync(
        InventoryBalanceListQuery listQuery,
        CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        IQueryable<InventoryBalance> query = dbContext.InventoryBalances
            .AsNoTracking()
            .Include(x => x.Warehouse)
            .Include(x => x.StorageLocation)
                .ThenInclude(x => x!.Zone)
            .Include(x => x.StockKeepingUnit);

        query = ApplyFilters(query, listQuery);

        var totalItems = await query.CountAsync(ct);

        query = ApplySorting(query, listQuery.SortBy, listQuery.SortDescending);

        var items = await query
            .Skip(listQuery.Skip)
            .Take(listQuery.Take)
            .ToListAsync(ct);

        return new ListResult<InventoryBalance>
        {
            Items = items,
            TotalItems = totalItems
        };
    }

    private static IQueryable<InventoryBalance> ApplyFilters(
        IQueryable<InventoryBalance> query,
        InventoryBalanceListQuery listQuery)
    {
        if (listQuery.WarehouseId is Guid warehouseId)
            query = query.Where(x => x.WarehouseId == warehouseId);

        if (listQuery.StorageLocationId is Guid storageLocationId)
            query = query.Where(x => x.StorageLocationId == storageLocationId);

        if (listQuery.StockKeepingUnitId is Guid stockKeepingUnitId)
            query = query.Where(x => x.StockKeepingUnitId == stockKeepingUnitId);

        if (!string.IsNullOrWhiteSpace(listQuery.SearchString))
        {
            query = query.Where(x =>
                x.Warehouse!.Name!.Contains(listQuery.SearchString)
                || x.StorageLocation!.Name!.Contains(listQuery.SearchString)
                || x.StockKeepingUnit!.Name!.Contains(listQuery.SearchString)
                || x.StockKeepingUnit.Code!.Contains(listQuery.SearchString));
        }

        return query;
    }

    private static IQueryable<InventoryBalance> ApplySorting(
        IQueryable<InventoryBalance> query,
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
            "Quantity" => sortDescending
                ? query.OrderByDescending(x => x.Quantity)
                : query.OrderBy(x => x.Quantity),
            "UpdatedAtUtc" => sortDescending
                ? query.OrderByDescending(x => x.UpdatedAtUtc)
                : query.OrderBy(x => x.UpdatedAtUtc),
            _ => query.OrderBy(x => x.Warehouse!.Name)
                .ThenBy(x => x.StorageLocation!.Name)
                .ThenBy(x => x.StockKeepingUnit!.Name)
        };
    }
}

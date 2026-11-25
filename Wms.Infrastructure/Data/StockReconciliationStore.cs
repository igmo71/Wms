using Microsoft.EntityFrameworkCore;
using Wms.Application.Reports;

namespace Wms.Data;

public sealed class StockReconciliationStore(IDbContextFactory<ApplicationDbContext> contextFactory)
    : IStockReconciliationStore
{
    public async Task<StockReconciliationQuantity[]> ReadBalancesAsync(CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        return await db.InventoryBalances.AsNoTracking()
            .Where(row => row.Quantity != 0m)
            .GroupBy(row => new { row.WarehouseId, row.StockKeepingUnitId })
            .Where(group => group.Sum(row => row.Quantity) != 0m)
            .Select(group => new StockReconciliationQuantity(group.Key.WarehouseId,
                group.Key.StockKeepingUnitId, group.Sum(row => row.Quantity)))
            .ToArrayAsync(ct);
    }

    public async Task<StockReconciliationWarehouse[]> ReadWarehousesAsync(CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        return await db.Warehouses.AsNoTracking()
            .Select(warehouse => new StockReconciliationWarehouse(warehouse.Id,
                string.IsNullOrWhiteSpace(warehouse.Name) ? warehouse.Id.ToString() : warehouse.Name))
            .ToArrayAsync(ct);
    }

    public async Task<IReadOnlyDictionary<Guid, StockReconciliationSku>> ReadSkusAsync(
        IEnumerable<Guid> skuIds, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var skus = new Dictionary<Guid, StockReconciliationSku>();
        foreach (var batch in skuIds.Chunk(500))
        {
            var rows = await db.StockKeepingUnits.AsNoTracking()
                .Where(sku => batch.Contains(sku.Id))
                .Select(sku => new StockReconciliationSku(sku.Id, sku.Name, sku.Code,
                    sku.BaseUnitOfMeasure == null ? null
                        : sku.BaseUnitOfMeasure.Description ?? sku.BaseUnitOfMeasure.Abbreviation
                            ?? sku.BaseUnitOfMeasure.Name))
                .ToArrayAsync(ct);
            foreach (var row in rows)
                skus.Add(row.Id, row);
        }

        return skus;
    }
}

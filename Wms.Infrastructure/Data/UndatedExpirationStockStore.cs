using Microsoft.EntityFrameworkCore;
using Wms.Application.Reports;

namespace Wms.Data;

public sealed class UndatedExpirationStockStore(
    IDbContextFactory<ApplicationDbContext> contextFactory) : IUndatedExpirationStockStore
{
    public async Task<UndatedExpirationStockPage> ListAsync(
        Guid? warehouseId, int skip, int take, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var balances = db.InventoryBalances.AsNoTracking()
            .Where(x => x.ExpirationDate == null && x.Quantity != 0m
                && x.StockKeepingUnit!.TracksExpirationDate);
        if (warehouseId is Guid id)
            balances = balances.Where(x => x.WarehouseId == id);

        var totalCount = await balances.CountAsync(ct);
        var rows = await balances
            .OrderBy(x => x.StockKeepingUnit!.Name).ThenBy(x => x.StockKeepingUnitId)
            .ThenBy(x => x.Warehouse!.Name).ThenBy(x => x.WarehouseId)
            .ThenBy(x => x.StorageLocation!.Code).ThenBy(x => x.StorageLocationId)
            .Select(x => new UndatedExpirationStockRow(
                x.WarehouseId, x.Warehouse!.Name,
                x.StorageLocationId, x.StorageLocation!.Code,
                x.StockKeepingUnitId, x.StockKeepingUnit!.Name,
                x.StockKeepingUnit.Code,
                x.StockKeepingUnit.BaseUnitOfMeasure == null ? null
                    : x.StockKeepingUnit.BaseUnitOfMeasure.Description
                        ?? x.StockKeepingUnit.BaseUnitOfMeasure.Abbreviation,
                x.Quantity))
            .Skip(skip).Take(take).ToArrayAsync(ct);
        return new UndatedExpirationStockPage(rows, totalCount);
    }
}

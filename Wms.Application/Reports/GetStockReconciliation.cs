using Wms.Application.Authorization;
using Wms.Common;

namespace Wms.Application.Reports;

public sealed record StockReconciliationQuantity(Guid WarehouseId, Guid SkuId, decimal Quantity);

public sealed record StockReconciliationWarehouse(Guid Id, string Name);

public sealed record StockReconciliationSku(Guid Id, string? Name, string? Code, string? UnitOfMeasure);

public sealed record StockReconciliationRow(
    Guid WarehouseId, string WarehouseName, Guid SkuId, string SkuName, string? SkuCode,
    string? UnitOfMeasure, bool SkuKnown, decimal WmsQuantity, decimal OneCQuantity)
{
    public decimal Difference => WmsQuantity - OneCQuantity;
}

public sealed record StockReconciliationSnapshot(
    IReadOnlyList<StockReconciliationWarehouse> Warehouses,
    IReadOnlyList<StockReconciliationRow> Rows,
    DateTimeOffset WmsReadAtUtc, DateTimeOffset OneCReadAtUtc);

public interface IStockReconciliationStore
{
    Task<StockReconciliationQuantity[]> ReadBalancesAsync(CancellationToken ct);
    Task<StockReconciliationWarehouse[]> ReadWarehousesAsync(CancellationToken ct);
    Task<IReadOnlyDictionary<Guid, StockReconciliationSku>> ReadSkusAsync(
        IEnumerable<Guid> skuIds, CancellationToken ct);
}

public interface IOneCStockBalanceReader
{
    Task<OperationResult<StockReconciliationQuantity[]>> ReadAsync(CancellationToken ct);
}

public sealed class GetStockReconciliation(
    IStockReconciliationStore store, IOneCStockBalanceReader oneCReader,
    WarehousePermission permission, TimeProvider clock)
{
    public async Task<OperationResult<StockReconciliationSnapshot>> ExecuteAsync(string? userId, CancellationToken ct)
    {
        var authorization = await permission.RequireManagerAsync(userId, ct);
        if (!authorization.IsSuccess)
            return authorization.Error!;

        var source = await oneCReader.ReadAsync(ct);
        if (!source.IsSuccess)
            return source.Error!;

        var oneCReadAt = clock.GetUtcNow();
        var wmsBalances = await store.ReadBalancesAsync(ct);
        var wmsReadAt = clock.GetUtcNow();

        var totals = wmsBalances.ToDictionary(
            row => (row.WarehouseId, row.SkuId), row => (Wms: row.Quantity, OneC: 0m));
        foreach (var row in source.Value!)
        {
            var key = (row.WarehouseId, row.SkuId);
            var total = totals.GetValueOrDefault(key);
            totals[key] = (total.Wms, total.OneC + row.Quantity);
        }

        var nonzero = totals.Where(pair => pair.Value.Wms != 0m || pair.Value.OneC != 0m).ToArray();
        var skus = await store.ReadSkusAsync(nonzero.Select(pair => pair.Key.SkuId).Distinct(), ct);
        var warehouses = await store.ReadWarehousesAsync(ct);
        var warehouseNames = warehouses.ToDictionary(warehouse => warehouse.Id, warehouse => warehouse.Name);

        var rows = nonzero.Select(pair =>
        {
            var (warehouseId, skuId) = pair.Key;
            var sku = skus.GetValueOrDefault(skuId);
            return new StockReconciliationRow(warehouseId,
                warehouseNames.GetValueOrDefault(warehouseId) ?? warehouseId.ToString(),
                skuId, sku?.Name ?? sku?.Code ?? skuId.ToString(), sku?.Code, sku?.UnitOfMeasure,
                sku is not null, pair.Value.Wms, pair.Value.OneC);
        }).ToArray();

        return new StockReconciliationSnapshot(warehouses, rows, wmsReadAt, oneCReadAt);
    }
}

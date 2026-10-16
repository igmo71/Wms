namespace Wms.Application.Reports;

public sealed record UndatedExpirationStockRow(
    Guid WarehouseId, string? WarehouseName,
    Guid StorageLocationId, string LocationCode,
    Guid SkuId, string? SkuName, string? SkuCode,
    string? UnitOfMeasure, decimal Quantity);

public sealed record UndatedExpirationStockPage(
    IReadOnlyList<UndatedExpirationStockRow> Items, int TotalCount);

public interface IUndatedExpirationStockStore
{
    Task<UndatedExpirationStockPage> ListAsync(
        Guid? warehouseId, int skip, int take, CancellationToken ct);
}

public sealed class GetUndatedExpirationStock(IUndatedExpirationStockStore store)
{
    public Task<UndatedExpirationStockPage> ExecuteAsync(
        Guid? warehouseId, int skip, int take, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(skip);
        ArgumentOutOfRangeException.ThrowIfLessThan(take, 1);
        return store.ListAsync(warehouseId, skip, take, ct);
    }
}

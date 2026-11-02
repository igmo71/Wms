namespace Wms.Application.OpeningImports;

public sealed record OpeningMixedStockViolation(
    Guid StorageLocationId, string Address, int DistinctSkuCount);

public interface IOpeningMixedStockViolationStore
{
    Task<IReadOnlyList<OpeningMixedStockViolation>> LoadViolationsAsync(
        Guid warehouseId, CancellationToken ct);
}

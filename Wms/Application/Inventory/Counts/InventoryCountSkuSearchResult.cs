namespace Wms.Application.Inventory.Counts;

public sealed record InventoryCountSkuSearchResult(
    Guid Id,
    string Code,
    string Name,
    string? UnitOfMeasure,
    bool IsExactMatch);

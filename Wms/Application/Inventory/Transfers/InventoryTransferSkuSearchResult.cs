namespace Wms.Application.Inventory.Transfers;

public sealed record InventoryTransferSkuSearchResult(
    Guid Id,
    string Code,
    string Name,
    string? UnitOfMeasure,
    double AvailableQuantity,
    bool IsExactMatch);

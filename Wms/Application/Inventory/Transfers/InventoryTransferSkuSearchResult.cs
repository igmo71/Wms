namespace Wms.Application.Inventory.Transfers;

public sealed record InventoryTransferSkuSearchResult(
    Guid Id,
    string Code,
    string Name,
    string? UnitOfMeasure,
    decimal AvailableQuantity,
    bool IsExactMatch);

using Wms.Common;

namespace Wms.Application.Inventory.Counts;

public sealed record StartInventoryCountCommand(Guid WarehouseId, Guid StorageLocationId,
    string? Barcode = null);

public sealed record IncrementInventoryCountSkuCommand(Guid InventoryCountId, string? Barcode,
    DateOnly? ExpirationDate = null, Guid? ConfirmedSkuId = null);

public sealed record SetInventoryCountQuantityCommand(Guid InventoryCountId, Guid ItemId,
    decimal CountedQuantity, bool ExpirationConfirmed = false);

public sealed record SetInventoryCountSkuQuantityCommand(Guid InventoryCountId, Guid StockKeepingUnitId,
    decimal CountedQuantity, DateOnly? ExpirationDate = null, bool ExpirationConfirmed = false);

public sealed record RemoveInventoryCountItemCommand(Guid InventoryCountId, Guid ItemId);

public sealed record InventoryCountExpirationRequired(Guid SkuId, string SkuName)
    : OperationError(OperationErrorType.Invalid, "Подтвердите дату на товаре или неизвестный срок.");

public sealed record InventoryCountSkuSearchResult(
    Guid Id, string Code, string Name, string? UnitOfMeasure, bool IsExactMatch, bool RequiresExpiration);

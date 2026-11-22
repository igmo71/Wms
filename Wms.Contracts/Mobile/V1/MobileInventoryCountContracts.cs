namespace Wms.Contracts.Mobile.V1;

public enum MobileInventoryCountStatus
{
    Draft = 0,
    Posted = 1
}

public sealed record MobileInventoryCountSummaryResponse(
    Guid Id,
    string Number,
    DateTime Date,
    Guid WarehouseId,
    string WarehouseName,
    MobileStorageLocationResponse StorageLocation,
    MobileInventoryCountStatus Status,
    int TotalItems,
    int CountedItems,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    DateTimeOffset? PostedAtUtc);

public sealed record MobileInventoryCountItemResponse(
    Guid Id,
    Guid StockKeepingUnitId,
    string SkuCode,
    string SkuName,
    string? UnitOfMeasure,
    decimal ExpectedQuantity,
    decimal? CountedQuantity,
    decimal? DifferenceQuantity,
    bool IsExpected,
    DateOnly? ExpirationDate,
    bool RequiresExpiration,
    bool ExpirationConfirmed);

public sealed record MobileInventoryCountDetailsResponse(
    MobileInventoryCountSummaryResponse Count,
    IReadOnlyList<MobileInventoryCountItemResponse> Items);

public sealed record MobileInventoryCountScanResponse(
    MobileInventoryCountDetailsResponse Details,
    MobileInventoryCountItemResponse? Item,
    MobileInventoryCountSkuConfirmation? Confirmation = null);

public sealed record MobileInventoryCountSkuConfirmation(Guid SkuId, string SkuName);

public sealed record MobileStartInventoryCountRequest(
    Guid ClientRequestId,
    Guid WarehouseId,
    string StorageLocationBarcode);

public sealed record MobileIncrementInventoryCountSkuRequest(
    Guid ClientRequestId,
    string Barcode,
    DateOnly? ExpirationDate,
    Guid? ConfirmedSkuId);

public sealed record MobileInventoryCountSkuSearchResponse(
    Guid Id,
    string Code,
    string Name,
    string? UnitOfMeasure,
    bool IsExactMatch,
    bool RequiresExpiration);

public sealed record MobileSetInventoryCountItemQuantityRequest(
    Guid ClientRequestId,
    decimal CountedQuantity,
    bool ExpirationConfirmed);

public sealed record MobileSetInventoryCountSkuQuantityRequest(
    Guid ClientRequestId,
    Guid StockKeepingUnitId,
    decimal CountedQuantity,
    DateOnly? ExpirationDate,
    bool ExpirationConfirmed);

public sealed record MobileInventoryCountCommandRequest(Guid ClientRequestId);

public sealed record MobileInventoryCountDeletedResponse(Guid InventoryCountId);


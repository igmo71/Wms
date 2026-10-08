namespace Wms.Contracts.Mobile.V1;

public sealed record MobileResolveLicensePlateNumberPutawayRequest(string Code);

public sealed record MobileResolveLicensePlateNumberPutawaySkuRequest(string Barcode);

public sealed record MobilePutawayLicensePlateNumberRequest(
    Guid ClientRequestId,
    Guid LicensePlateNumberItemId,
    decimal Quantity,
    string DestinationStorageLocationBarcode);

public sealed record MobileLicensePlateNumberPutawayItemResponse(
    Guid Id,
    Guid StockKeepingUnitId,
    string SkuCode,
    string SkuName,
    string? UnitOfMeasure,
    decimal AcceptedQuantity,
    decimal PlacedQuantity,
    decimal RemainingQuantity)
{
    public bool IsPartiallyPlaced => PlacedQuantity > 0 && RemainingQuantity > 0;
    public bool IsFullyPlaced => RemainingQuantity == 0;
}

public enum MobileLicensePlateNumberPutawayRecommendationCategory
{
    CurrentSkuStock = 1,
    EmptyWithSkuBalanceHistory = 2
}

public sealed record MobileLicensePlateNumberPutawayRecommendationResponse(
    Guid StorageLocationId,
    string Address,
    string Name,
    MobileLicensePlateNumberPutawayRecommendationCategory Category,
    decimal? CurrentSkuQuantity);

public sealed record MobileLicensePlateNumberPutawaySelectionResponse(
    MobileLicensePlateNumberPutawayItemResponse Item,
    IReadOnlyList<MobileLicensePlateNumberPutawayRecommendationResponse> Recommendations);

public sealed record MobileLicensePlateNumberPutawayHistoryResponse(
    Guid Id,
    Guid LicensePlateNumberItemId,
    Guid StockKeepingUnitId,
    string SkuName,
    string? UnitOfMeasure,
    decimal Quantity,
    string DestinationAddress,
    string DestinationName,
    string CreatedBy,
    DateTimeOffset CreatedAtUtc,
    Guid InventoryMovementId,
    string? CreatedByName = null);

public sealed record MobileLicensePlateNumberPutawayDetailsResponse(
    Guid Id,
    string Code,
    string OrderNumber,
    Guid WarehouseId,
    bool CanPutaway,
    string? BlockedReason,
    bool IsCompleted,
    IReadOnlyList<MobileLicensePlateNumberPutawayItemResponse> Items,
    IReadOnlyList<MobileLicensePlateNumberPutawayHistoryResponse> History);

public sealed record MobileLicensePlateNumberPutawayCommandResponse(
    MobileLicensePlateNumberPutawayDetailsResponse Details,
    Guid PutawayId);

namespace Wms.Application.LicensePlateNumbers;

public sealed record LicensePlateNumberPutawayDetails(
    Guid Id,
    string Code,
    string OrderNumber,
    Guid WarehouseId,
    bool CanPutaway,
    string? BlockedReason,
    bool IsCompleted,
    IReadOnlyList<LicensePlateNumberPutawayItem> Items,
    IReadOnlyList<LicensePlateNumberPutawayHistoryItem> History);

public sealed record LicensePlateNumberPutawayItem(
    Guid Id,
    Guid StockKeepingUnitId,
    string SkuCode,
    string SkuName,
    string? UnitOfMeasure,
    decimal AcceptedQuantity,
    decimal PlacedQuantity,
    decimal RemainingQuantity);

public enum LicensePlateNumberPutawayRecommendationCategory
{
    CurrentSkuStock = 1,
    EmptyWithSkuBalanceHistory = 2,
    Empty = 3
}

public sealed record LicensePlateNumberPutawayRecommendation(
    Guid StorageLocationId,
    string Address,
    string Name,
    LicensePlateNumberPutawayRecommendationCategory Category);

public sealed record LicensePlateNumberPutawaySelection(
    LicensePlateNumberPutawayItem Item,
    IReadOnlyList<LicensePlateNumberPutawayRecommendation> Recommendations);

public sealed record LicensePlateNumberPutawayHistoryItem(
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

public sealed record PutawayLicensePlateNumberCommand(
    Guid LicensePlateNumberId,
    Guid LicensePlateNumberItemId,
    Guid DestinationStorageLocationId,
    decimal Quantity);

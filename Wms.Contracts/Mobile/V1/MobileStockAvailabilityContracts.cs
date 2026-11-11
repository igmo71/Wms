namespace Wms.Contracts.Mobile.V1;

public sealed record MobileSkuLocationResponse(string LocationCode,
    DateOnly? ExpirationDate, decimal AvailableQuantity,
    decimal AccountingQuantity);

public sealed record MobileStockAvailabilityResponse(Guid WarehouseId,
    Guid SkuId, IReadOnlyList<MobileSkuLocationResponse> Locations);

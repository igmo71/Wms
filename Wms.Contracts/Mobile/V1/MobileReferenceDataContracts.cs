namespace Wms.Contracts.Mobile.V1;

public enum MobileStorageLocationContext
{
    AnyOperational = 0,
    Storage = 1,
    Transit = 2,
    Receiving = 3,
    Shipping = 4
}

public sealed record MobileResolveStorageLocationRequest(
    string Barcode,
    Guid? ExpectedWarehouseId = null,
    MobileStorageLocationContext Context = MobileStorageLocationContext.AnyOperational);

public sealed record MobileStorageLocationResponse(
    Guid Id,
    string Name,
    string Address,
    Guid WarehouseId,
    string WarehouseName,
    Guid ZoneId,
    string ZoneName,
    MobileStorageLocationContext ZoneType);

public sealed record MobileStorageLocationSearchResponse(
    IReadOnlyList<MobileStorageLocationResponse> Locations,
    bool HasMore);

public sealed record MobileResolveSkuRequest(string Barcode);

public sealed record MobileSkuResponse(
    Guid Id,
    string Code,
    string Name,
    string? UnitOfMeasure);

public sealed record MobileWarehouseResponse(Guid Id, string Name);


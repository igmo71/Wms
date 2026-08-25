namespace Wms.Contracts.Mobile.V1;

public static class MobileApiRoutes
{
    public const string Base = "/api/mobile/v1";
    public const string Login = Base + "/auth/login";
    public const string Refresh = Base + "/auth/refresh";
    public const string Me = Base + "/me";
    public const string ResolveStorageLocation = Base + "/barcodes/storage-location/resolve";
    public const string ResolveSku = Base + "/barcodes/sku/resolve";
}

public sealed record MobileLoginRequest(string Email, string Password);

public sealed record MobileRefreshRequest(string RefreshToken);

public sealed record MobileSessionResponse(
    string TokenType,
    string AccessToken,
    long ExpiresIn,
    string RefreshToken);

public sealed record MobileCurrentUserResponse(
    string Id,
    string DisplayName,
    string Email);

public sealed record MobileProblemResponse(string Code, string Message);

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

public sealed record MobileResolveSkuRequest(string Barcode);

public sealed record MobileSkuResponse(
    Guid Id,
    string Code,
    string Name,
    string? UnitOfMeasure);

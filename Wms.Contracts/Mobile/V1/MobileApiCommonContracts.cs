namespace Wms.Contracts.Mobile.V1;

public static class MobileApiRoutes
{
    public const string Base = "/api/mobile/v1";
    public const string Login = Base + "/auth/login";
    public const string Refresh = Base + "/auth/refresh";
    public const string Me = Base + "/me";
    public const string ResolveStorageLocation = Base + "/barcodes/storage-location/resolve";
    public const string ResolveSku = Base + "/barcodes/sku/resolve";
    public const string Warehouses = Base + "/warehouses";
    public const string InventoryTransfers = Base + "/inventory-transfers";
    public const string InventoryCounts = Base + "/inventory-counts";
    public const string ReceivingOrders = Base + "/receiving-orders";
    public const string LicensePlateNumberPutaway = Base + "/lpn-putaway";
    public const string ShippingOrders = Base + "/shipping-orders";
}

public static class MobileProblemCodes
{
    public const string InvalidCommand = "invalid_command";
    public const string ResourceNotFound = "resource_not_found";
    public const string RequestConflict = "request_conflict";
    public const string CommandFailed = "command_failed";
}
public enum MobileOrderSynchronizationLevel
{
    Synchronized = 0,
    RequiresOperatorDecision = 1,
    Blocking = 2
}

public sealed record MobileOrderSynchronizationResponse(
    MobileOrderSynchronizationLevel Level,
    bool IsFresh,
    IReadOnlyList<string> ChangedFields,
    bool CommentChanged,
    string? OneCComment,
    string? VerificationError,
    string? Fingerprint = null,
    bool RequiresManager = false,
    MobileOrderSynchronizationLevel? SourceLevel = null);


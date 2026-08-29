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

public sealed record MobileWarehouseResponse(Guid Id, string Name);

public enum MobileInventoryTransferStatus
{
    Draft = 0,
    InProgress = 1,
    Completed = 2
}

public sealed record MobileInventoryTransferSummaryResponse(
    Guid Id,
    string Number,
    DateTime Date,
    Guid WarehouseId,
    string WarehouseName,
    MobileInventoryTransferStatus Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    MobileStorageLocationResponse? TransitStorageLocation);

public sealed record MobileInventoryTransferMovementResponse(
    Guid MovementId,
    Guid StockKeepingUnitId,
    string SkuCode,
    string SkuName,
    string? UnitOfMeasure,
    double Quantity,
    MobileInventoryMovementLocationResponse Source,
    MobileInventoryMovementLocationResponse Destination);

public sealed record MobileInventoryTransferDetailsResponse(
    MobileInventoryTransferSummaryResponse Transfer,
    IReadOnlyList<MobileInventoryTransferMovementResponse> Movements,
    IReadOnlyList<MobileInventoryTransferSkuBalanceResponse> TransitBalances);

public sealed record MobileInventoryTransferSkuBalanceResponse(
    Guid StockKeepingUnitId,
    string SkuCode,
    string SkuName,
    string? UnitOfMeasure,
    double Quantity);

public sealed record MobileCreateInventoryTransferRequest(
    Guid ClientRequestId,
    Guid WarehouseId,
    Guid? TransitStorageLocationId = null);

public sealed record MobileResolveDirectTransferSkuRequest(
    string Barcode,
    Guid SourceStorageLocationId);

public sealed record MobileDirectTransferSkuResponse(
    Guid Id,
    string Code,
    string Name,
    string? UnitOfMeasure,
    double AvailableQuantity);

public sealed record MobileDirectTransferSkuSearchResponse(
    Guid Id,
    string Code,
    string Name,
    string? UnitOfMeasure,
    double AvailableQuantity,
    bool IsExactMatch);

public sealed record MobileResolveTransitTransferSkuRequest(string Barcode);

public sealed record MobilePickToTransitRequest(
    Guid ClientRequestId,
    Guid SourceStorageLocationId,
    Guid StockKeepingUnitId,
    double Quantity);

public sealed record MobilePutFromTransitRequest(
    Guid ClientRequestId,
    Guid DestinationStorageLocationId,
    Guid StockKeepingUnitId,
    double Quantity);

public sealed record MobileTransitInventoryTransferMovementResponse(
    Guid MovementId,
    Guid TransferId,
    MobileInventoryTransferStatus TransferStatus);

public sealed record MobileMoveDirectInventoryTransferRequest(
    Guid ClientRequestId,
    Guid SourceStorageLocationId,
    Guid DestinationStorageLocationId,
    Guid StockKeepingUnitId,
    double Quantity);

public sealed record MobileInventoryMovementLocationResponse(
    Guid Id,
    string Address,
    string Name);

public sealed record MobileMoveDirectInventoryTransferResponse(
    Guid MovementId,
    Guid TransferId,
    int LineNumber,
    Guid StockKeepingUnitId,
    string SkuCode,
    string SkuName,
    string? UnitOfMeasure,
    double Quantity,
    MobileInventoryMovementLocationResponse Source,
    MobileInventoryMovementLocationResponse Destination,
    DateTimeOffset PostedAtUtc,
    MobileInventoryTransferStatus TransferStatus);

public sealed record MobileCompleteInventoryTransferRequest(Guid ClientRequestId);

public sealed record MobileCompleteInventoryTransferResponse(
    Guid TransferId,
    MobileInventoryTransferStatus Status,
    DateTimeOffset CompletedAtUtc);

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
    double ExpectedQuantity,
    double? CountedQuantity,
    double? DifferenceQuantity,
    bool IsExpected);

public sealed record MobileInventoryCountDetailsResponse(
    MobileInventoryCountSummaryResponse Count,
    IReadOnlyList<MobileInventoryCountItemResponse> Items);

public sealed record MobileInventoryCountScanResponse(
    MobileInventoryCountDetailsResponse Details,
    MobileInventoryCountItemResponse Item);

public sealed record MobileStartInventoryCountRequest(
    Guid ClientRequestId,
    Guid WarehouseId,
    string StorageLocationBarcode);

public sealed record MobileIncrementInventoryCountSkuRequest(
    Guid ClientRequestId,
    string Barcode);

public sealed record MobileInventoryCountSkuSearchResponse(
    Guid Id,
    string Code,
    string Name,
    string? UnitOfMeasure,
    bool IsExactMatch);

public sealed record MobileSetInventoryCountItemQuantityRequest(
    Guid ClientRequestId,
    double CountedQuantity);

public sealed record MobileSetInventoryCountSkuQuantityRequest(
    Guid ClientRequestId,
    Guid StockKeepingUnitId,
    double CountedQuantity);

public sealed record MobileInventoryCountCommandRequest(Guid ClientRequestId);

public sealed record MobileInventoryCountDeletedResponse(Guid InventoryCountId);

public enum MobileReceivingOrderStatus
{
    ReadyForReceiving = 1,
    InReceiving = 2,
    ProcessingRequired = 3,
    Received = 4
}

public enum MobilePutawayStatus
{
    Inactive = 0,
    Pending = 1,
    InProgress = 2,
    Completed = 3
}

public sealed record MobileReceivingOrderLocationResponse(
    Guid Id,
    string Name,
    string Address,
    Guid ZoneId,
    string ZoneName);

public sealed record MobileReceivingOrderProgressResponse(
    int TotalLineCount,
    int ConfirmedLineCount,
    int PositiveLineCount,
    int FullyAllocatedLineCount,
    double PlanQuantity,
    double FactQuantity,
    double AllocatedQuantity);

public sealed record MobileReceivingOrderSummaryResponse(
    Guid Id,
    string Number,
    DateTime Date,
    Guid WarehouseId,
    string WarehouseName,
    string ShipperName,
    string Queue,
    string WarehouseOperation,
    string BusinessOperation,
    MobileReceivingOrderStatus Status,
    MobilePutawayStatus PutawayStatus,
    string? Comment,
    MobileReceivingOrderLocationResponse? ReceivingLocation,
    MobileReceivingOrderProgressResponse Progress,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    DateTimeOffset? PutawayStartedAtUtc,
    DateTimeOffset? PutawayCompletedAtUtc);

public sealed record MobileReceivingOrderLineResponse(
    int LineNumber,
    Guid StockKeepingUnitId,
    string SkuCode,
    string SkuName,
    string? UnitOfMeasure,
    double PlanQuantity,
    double? FactQuantity,
    double? DifferenceQuantity,
    double AllocatedQuantity,
    double? RemainingPutawayQuantity,
    string? Comment);

public sealed record MobileReceivingOrderMovementResponse(
    Guid Id,
    int LineNumber,
    Guid StockKeepingUnitId,
    double Quantity,
    MobileReceivingOrderLocationResponse Destination,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    DateTimeOffset? PostedAtUtc);

public sealed record MobileReceivingOrderDetailsResponse(
    MobileReceivingOrderSummaryResponse Order,
    IReadOnlyList<MobileReceivingOrderLineResponse> Lines,
    IReadOnlyList<MobileReceivingOrderMovementResponse> Movements);

public sealed record MobileReceivingOrderWorkQueueResponse(
    IReadOnlyList<MobileReceivingOrderSummaryResponse> Receiving,
    IReadOnlyList<MobileReceivingOrderSummaryResponse> Putaway);

public sealed record MobileReceivingOrderLineCandidateResponse(
    int LineNumber,
    Guid StockKeepingUnitId,
    string SkuCode,
    string SkuName,
    string? UnitOfMeasure,
    double PlanQuantity,
    double? FactQuantity,
    double AllocatedQuantity,
    double? RemainingPutawayQuantity,
    bool IsExactMatch);

public sealed record MobileReceivingOrderLineSearchResponse(
    IReadOnlyList<MobileReceivingOrderLineCandidateResponse> Items,
    bool HasMore);

public sealed record MobileResolveReceivingOrderDocumentRequest(
    Guid WarehouseId,
    string Barcode);

public sealed record MobileResolveReceivingOrderSkuRequest(string Barcode);

public sealed record MobileStartReceivingOrderRequest(
    Guid ClientRequestId,
    string ReceivingLocationBarcode);

public sealed record MobileReceivingOrderCommandRequest(Guid ClientRequestId);

public sealed record MobileSetReceivingOrderLineQuantityRequest(
    Guid ClientRequestId,
    double Quantity);

public sealed record MobileAddReceivingOrderPutawayMovementRequest(
    Guid ClientRequestId,
    int LineNumber,
    string DestinationStorageLocationBarcode,
    double Quantity);

public sealed record MobileReceivingOrderCommandResponse(
    MobileReceivingOrderDetailsResponse Details,
    int? ChangedLineNumber = null,
    Guid? ChangedMovementId = null);

namespace Wms.Contracts.Mobile.V1;

public enum MobileReceivingOrderStatus
{
    Unknown = 0,
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
    decimal PlanQuantity,
    decimal FactQuantity,
    decimal AllocatedQuantity);

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
    MobileOrderSynchronizationResponse Synchronization,
    string? Comment,
    MobileReceivingOrderLocationResponse? ReceivingLocation,
    MobileReceivingOrderProgressResponse Progress,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    DateTimeOffset? PutawayStartedAtUtc,
    DateTimeOffset? PutawayCompletedAtUtc,
    bool IsParticipant,
    bool CanJoin,
    string? JoinBlockedReason);

public sealed record MobileReceivingOrderLineResponse(
    int LineNumber,
    Guid StockKeepingUnitId,
    string SkuCode,
    string SkuName,
    string? UnitOfMeasure,
    decimal PlanQuantity,
    decimal? FactQuantity,
    decimal? DifferenceQuantity,
    decimal AllocatedQuantity,
    decimal? RemainingPutawayQuantity,
    string? Comment);

public sealed record MobileReceivingOrderMovementResponse(
    Guid Id,
    int LineNumber,
    Guid StockKeepingUnitId,
    decimal Quantity,
    MobileReceivingOrderLocationResponse Destination,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    DateTimeOffset? PostedAtUtc);

public sealed record MobileReceivingOrderDetailsResponse(
    MobileReceivingOrderSummaryResponse Order,
    IReadOnlyList<MobileReceivingOrderLineResponse> Lines,
    IReadOnlyList<MobileReceivingOrderMovementResponse> Movements);

public sealed record MobileReceivingOrderWorkQueueResponse(
    IReadOnlyList<MobileReceivingOrderSummaryResponse> Personal,
    IReadOnlyList<MobileReceivingOrderSummaryResponse> Available);

public sealed record MobileReceivingOrderLineCandidateResponse(
    int LineNumber,
    Guid StockKeepingUnitId,
    string SkuCode,
    string SkuName,
    string? UnitOfMeasure,
    decimal PlanQuantity,
    decimal? FactQuantity,
    decimal AllocatedQuantity,
    decimal? RemainingPutawayQuantity,
    bool IsExactMatch);

public sealed record MobileReceivingOrderLineSearchResponse(
    IReadOnlyList<MobileReceivingOrderLineCandidateResponse> Items,
    bool HasMore);

public sealed record MobileResolveReceivingOrderDocumentRequest(
    Guid WarehouseId,
    string Barcode);

public sealed record MobileResolveReceivingOrderSkuRequest(string Barcode);

public sealed record MobileJoinReceivingOrderRequest(
    Guid ClientRequestId,
    string? ReceivingLocationBarcode);

public sealed record MobileReceivingOrderCommandRequest(Guid ClientRequestId);

public sealed record MobileSetReceivingOrderLineQuantityRequest(
    Guid ClientRequestId,
    decimal Quantity);

public sealed record MobileAddReceivingOrderPutawayMovementRequest(
    Guid ClientRequestId,
    int LineNumber,
    string DestinationStorageLocationBarcode,
    decimal Quantity);

public sealed record MobileReceivingOrderCommandResponse(
    MobileReceivingOrderDetailsResponse Details,
    int? ChangedLineNumber = null,
    Guid? ChangedMovementId = null);


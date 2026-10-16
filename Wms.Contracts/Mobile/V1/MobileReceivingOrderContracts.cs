namespace Wms.Contracts.Mobile.V1;

public enum MobileReceivingOrderStatus
{
    Unknown = 0,
    ReadyForReceiving = 1,
    InReceiving = 2,
    ProcessingRequired = 3,
    Received = 4
}

public enum MobileLicensePlateNumberStatus
{
    Available = 0,
    Open = 1,
    Closed = 2,
    Cancelled = 3
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
    int PositiveLineCount);

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
    MobileOrderSynchronizationResponse Synchronization,
    string? Comment,
    MobileReceivingOrderLocationResponse? ReceivingLocation,
    MobileReceivingOrderProgressResponse Progress,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
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
    string? Comment);

public sealed record MobileReceivingOrderLicensePlateNumberResponse(
    Guid Id,
    string Code,
    MobileLicensePlateNumberStatus Status,
    string StatusName,
    string ResponsibleUserName,
    string CreatedByName,
    DateTimeOffset CreatedAtUtc,
    string? ClosedByName,
    DateTimeOffset? ClosedAtUtc,
    string? ReopenedByName,
    DateTimeOffset? ReopenedAtUtc,
    DateTimeOffset? CancelledAtUtc,
    bool IsOwnedByCurrentUser,
    bool CanEdit,
    bool CanCancel,
    bool CanClose,
    bool CanReopen,
    IReadOnlyList<MobileReceivingOrderLicensePlateNumberItemResponse> Items);

public sealed record MobileReceivingOrderLicensePlateNumberItemResponse(
    Guid StockKeepingUnitId,
    string SkuCode,
    string SkuName,
    string? UnitOfMeasure,
    decimal Quantity,
    string CreatedByName,
    DateTimeOffset CreatedAtUtc,
    string? UpdatedByName,
    DateTimeOffset? UpdatedAtUtc);

public sealed record MobileReceivingOrderDetailsResponse(
    MobileReceivingOrderSummaryResponse Order,
    IReadOnlyList<MobileReceivingOrderLineResponse> Lines,
    IReadOnlyList<MobileReceivingOrderLicensePlateNumberResponse> LicensePlateNumbers);

public sealed record MobileReceivingOrderWorkQueueResponse(
    IReadOnlyList<MobileReceivingOrderSummaryResponse> Personal);

public sealed record MobileReceivingOrderLineCandidateResponse(
    int LineNumber,
    Guid StockKeepingUnitId,
    string SkuCode,
    string SkuName,
    string? UnitOfMeasure,
    decimal PlanQuantity,
    decimal? FactQuantity,
    bool IsExactMatch);

public sealed record MobileResolveReceivingOrderDocumentRequest(
    Guid WarehouseId,
    string Barcode);

public sealed record MobileResolveReceivingOrderSkuRequest(string Barcode);

public sealed record MobileJoinReceivingOrderRequest(
    Guid ClientRequestId,
    string? ReceivingLocationBarcode);

public sealed record MobileCompleteReceivingOrderRequest(
    Guid ClientRequestId,
    string ExpectedFingerprint);

public sealed record MobileOpenReceivingLicensePlateNumberRequest(
    Guid ClientRequestId,
    string Code);

public sealed record MobileReceivingOrderCommandRequest(Guid ClientRequestId);

public sealed record MobileSetReceivingLicensePlateNumberItemQuantityRequest(
    Guid ClientRequestId,
    decimal Quantity);

public sealed record MobileReceivingOrderCommandResponse(
    MobileReceivingOrderDetailsResponse Details,
    Guid? SelectedLicensePlateNumberId = null);

// Replacement receiving transport. The earlier DTOs remain until Mobile switches in T022.
public sealed record MobileReceivingSourceLineResponse(
    int LineNumber, Guid? SourceSkuRefKey, Guid? StockKeepingUnitId, string? RawQuantity,
    decimal? Quantity, string? Error);

public sealed record MobileReceivingSourceResponse(
    Guid RefKey, Guid? WarehouseId, string? Number,
    bool? Posted, bool? DeletionMark, Guid? VersionId,
    DateTimeOffset? LastSuccessfulCheckAtUtc, DateTimeOffset? LastAttemptAtUtc,
    string? LastAttemptError, bool HasUnknownSku,
    IReadOnlyDictionary<Guid, decimal?> PlanBySku,
    IReadOnlyList<MobileReceivingSourceLineResponse> Lines);

public sealed record MobileReceivingWorkItemResponse(
    Guid SkuId, string? SkuCode, string? SkuName,
    bool TracksExpirationDate, decimal? PlanQuantity, decimal? FactQuantity);

public sealed record MobileReceivingWorkLpnItemResponse(
    Guid Id, Guid SkuId, string? SkuCode, string? SkuName,
    decimal Quantity, DateOnly? ExpirationDate,
    decimal PlacedQuantity, decimal RemainingQuantity);

public sealed record MobileReceivingWorkPutawayResponse(
    Guid Id, Guid ItemId, Guid DestinationStorageLocationId,
    decimal Quantity, DateTimeOffset CreatedAtUtc, Guid MovementId);

public sealed record MobileReceivingWorkLpnResponse(
    Guid Id, string Code, Guid OrderId, Guid WarehouseId,
    MobileLicensePlateNumberStatus Status, string? ResponsibleUserId,
    Guid? OriginalReceivingLocationId, long Revision,
    IReadOnlyList<MobileReceivingWorkLpnItemResponse> Items,
    IReadOnlyList<MobileReceivingWorkPutawayResponse> Putaways);

public sealed record MobileReceivingWorkOrderResponse(
    Guid Id, string? Number, Guid WarehouseId, MobileReceivingOrderStatus Status,
    int? ActiveCycleNumber, Guid? ReceivingLocationId,
    bool IsParticipant, long Revision,
    IReadOnlyList<Guid> SourceRefKeys,
    IReadOnlyList<MobileReceivingWorkItemResponse> Items,
    IReadOnlyList<MobileReceivingWorkLpnResponse> Lpns);

public sealed record MobileReceivingSkuCandidateResponse(
    Guid SkuId, string? Name, string? Code,
    bool TracksExpirationDate, bool IsInOrder,
    decimal? CurrentPlanQuantity, decimal? FactQuantity);

public sealed record MobileReceivingCreateOrderRequest(
    IReadOnlyList<Guid> SourceRefKeys);

public sealed record MobileReceivingResolveDocumentRequest(string Barcode);

public sealed record MobileReceivingDocumentResolutionResponse(
    MobileReceivingSourceResponse? Source,
    MobileReceivingWorkOrderResponse? Order);

public sealed record MobileReceivingBarcodeRequest(string Barcode);

public sealed record MobileReceivingCommandRequest(Guid ClientRequestId);

public sealed record MobileReceivingJoinCycleRequest(
    Guid ClientRequestId, Guid? ReceivingLocationId);

public sealed record MobileReceivingOpenLpnRequest(
    Guid ClientRequestId, string LpnCode);

public sealed record MobileReceivingEditLpnRequest(
    Guid ClientRequestId, Guid SkuId, decimal Quantity,
    DateOnly? ExpirationDate);

public sealed record MobileReceivingCloseLpnRequest(
    Guid ClientRequestId, bool ConfirmExpiredDate,
    string? ManagerReason);

public sealed record MobileReceivingPutawayRequest(
    Guid ClientRequestId, Guid LpnItemId,
    Guid DestinationStorageLocationId, decimal Quantity);

public enum MobileReceivingPutawayRecommendationKind
{
    CurrentSkuStock,
    EmptyWithSkuBalanceHistory
}

public sealed record MobileReceivingPutawayRecommendationResponse(
    Guid StorageLocationId, string Address, string Name,
    MobileReceivingPutawayRecommendationKind Kind,
    decimal? CurrentSkuQuantity);

public sealed record MobileReceivingPutawaySelectionResponse(
    MobileReceivingWorkLpnItemResponse Item,
    IReadOnlyList<MobileReceivingPutawayRecommendationResponse> Recommendations);

public sealed record MobileReceivingCommandIdResponse(Guid ResourceId);

public sealed record MobileReceivingCloseResultResponse(
    Guid LpnId, decimal? UsedPlan, decimal TotalFact,
    bool SourceCheckSucceeded, string? SourceCheckError,
    DateTimeOffset? LastSuccessfulCheckAtUtc,
    bool ManagerException, bool ExpiredDateConfirmed);


namespace Wms.Contracts.Mobile.V1;

public enum MobileReceivingOrderStatus
{
    ReadyForReceiving = 0,
    InReceiving = 1,
    Received = 2
}

public enum MobileLicensePlateNumberStatus
{
    Available = 0,
    Open = 1,
    Closed = 2,
    Cancelled = 3,
    Deleted = 4
}

public sealed record MobileReceivingSourceLineResponse(
    int LineNumber, Guid? SourceSkuRefKey, Guid? StockKeepingUnitId,
    string? RawQuantity, decimal? Quantity, string? Error);

public sealed record MobileReceivingSourceResponse(
    Guid RefKey, Guid? WarehouseId, string? Number,
    bool? Posted, bool? DeletionMark, Guid? VersionId,
    DateTimeOffset? LastSuccessfulCheckAtUtc, DateTimeOffset? LastAttemptAtUtc,
    string? LastAttemptError, bool HasUnknownSku,
    IReadOnlyDictionary<Guid, decimal?> PlanBySku,
    IReadOnlyList<MobileReceivingSourceLineResponse> Lines)
{
    public string? WarehouseName { get; init; }
    public IReadOnlyDictionary<Guid, string> SkuNames { get; init; } = new Dictionary<Guid, string>();
}

public sealed record MobileReceivingWorkItemResponse(
    Guid SkuId, string? SkuCode, string? SkuName,
    bool TracksExpirationDate, string? UnitOfMeasure,
    decimal? PlanQuantity, decimal? FactQuantity)
{
    public bool IsCurrent { get; init; } = true;
    public bool HasBarcode { get; init; }
}

public sealed record MobileReceivingWorkLpnItemResponse(
    Guid Id, Guid SkuId, string? SkuCode, string? SkuName,
    string? UnitOfMeasure, decimal Quantity, DateOnly? ExpirationDate,
    decimal PlacedQuantity, decimal RemainingQuantity);

public sealed record MobileReceivingWorkPutawayResponse(
    Guid Id, Guid ItemId, Guid DestinationStorageLocationId,
    decimal Quantity, DateTimeOffset CreatedAtUtc, Guid MovementId)
{
    public string? DestinationAddress { get; init; }
}

public sealed record MobileReceivingWorkLpnResponse(
    Guid Id, string Code, Guid OrderId, Guid WarehouseId,
    MobileLicensePlateNumberStatus Status, string? ResponsibleUserId,
    Guid? OriginalReceivingLocationId, long Revision,
    IReadOnlyList<MobileReceivingWorkLpnItemResponse> Items,
    IReadOnlyList<MobileReceivingWorkPutawayResponse> Putaways)
{
    public string? ResponsibleUserName { get; init; }
}

public sealed record MobileReceivingOrderQueueResponse(
    Guid Id, string Number, DateTimeOffset CreatedAtUtc,
    Guid WarehouseId, MobileReceivingOrderStatus Status);

public sealed record MobileReceivingWorkOrderResponse(
    Guid Id, string Number, DateTimeOffset CreatedAtUtc,
    Guid WarehouseId, MobileReceivingOrderStatus Status,
    int ActiveCycleNumber, Guid? ReceivingLocationId,
    bool IsParticipant, long Revision,
    IReadOnlyList<Guid> SourceRefKeys,
    IReadOnlyList<MobileReceivingWorkItemResponse> Items,
    IReadOnlyList<MobileReceivingWorkLpnResponse> Lpns)
{
    public string? WarehouseName { get; init; }
    public string? ReceivingLocationAddress { get; init; }
    public bool MayCompleteOrder { get; init; }
}

public sealed record MobileReceivingSkuCandidateResponse(
    Guid SkuId, string? Name, string? Code,
    bool TracksExpirationDate, bool IsInOrder,
    decimal? CurrentPlanQuantity, decimal? FactQuantity);

public sealed record MobileReceivingCreateOrderRequest(
    Guid ClientRequestId, IReadOnlyList<Guid> SourceRefKeys);
public sealed record MobileReceivingResolveDocumentRequest(string Barcode);
public sealed record MobileReceivingAttachSourceRequest(Guid ClientRequestId, string Barcode);
public sealed record MobileReceivingLinkedSourceResponse(
    Guid RefKey, string? Number, string? Date, string? Sender,
    string? SenderType, string? WarehouseOperation, string? BusinessOperation,
    string? BasisDocument, string? BasisDocumentType)
{
    public string? SenderName { get; init; }
    public string? LastAttemptError { get; init; }
}
public sealed record MobileReceivingDocumentResolutionResponse(
    MobileReceivingSourceResponse? Source, MobileReceivingWorkOrderResponse? Order);
public sealed record MobileReceivingBarcodeRequest(string Barcode);
public sealed record MobileReceivingAddBarcodeRequest(Guid ClientRequestId, string Barcode);
public sealed record MobileReceivingCommandRequest(Guid ClientRequestId);
public sealed record MobileReceivingJoinCycleRequest(
    Guid ClientRequestId, Guid? ReceivingLocationId);
public sealed record MobileReceivingOpenLpnRequest(
    Guid ClientRequestId, string LpnCode);
public sealed record MobileReceivingEditLpnRequest(
    Guid ClientRequestId, Guid SkuId, decimal Quantity, DateOnly? ExpirationDate);
public sealed record MobileReceivingClearLpnItemRequest(Guid ClientRequestId, long ExpectedRevision);
public sealed record MobileReceivingCloseLpnRequest(
    Guid ClientRequestId, string? ManagerReason);
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
    bool ManagerException);

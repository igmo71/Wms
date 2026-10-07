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
    int PositiveLineCount,
    decimal PlanQuantity,
    decimal FactQuantity);

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


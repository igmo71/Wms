namespace Wms.Contracts.Mobile.V1;

public enum MobileShippingOrderStatus
{
    Prepared = 1,
    ReadyForPicking = 2,
    ReadyForVerification = 3,
    InVerification = 4,
    Verified = 5,
    ReadyForShipment = 6,
    Shipped = 7
}

public sealed record MobileShippingOrderLocationResponse(
    Guid Id,
    string Name,
    string Address,
    Guid ZoneId,
    string ZoneName);

public sealed record MobileShippingOrderProgressResponse(
    int TotalLineCount,
    int FullyPickedLineCount,
    int PartiallyPickedLineCount,
    int ZeroPickedLineCount);

public sealed record MobileShippingOrderSummaryResponse(
    Guid Id,
    string Number,
    DateTime Date,
    Guid WarehouseId,
    string WarehouseName,
    string ReceiverName,
    string Queue,
    string WarehouseOperation,
    MobileShippingOrderStatus Status,
    MobileOrderSynchronizationResponse Synchronization,
    string? Comment,
    DateTime? PlannedShippingDate,
    string? DeliveryDirection,
    MobileShippingOrderLocationResponse? ShippingLocation,
    MobileShippingOrderProgressResponse Progress,
    DateTimeOffset? PickingStartedAtUtc,
    DateTimeOffset? ReadyForShipmentAtUtc,
    DateTimeOffset? ShippedAtUtc);

public sealed record MobileShippingOrderLineResponse(
    int LineNumber,
    Guid StockKeepingUnitId,
    string SkuCode,
    string SkuName,
    string? UnitOfMeasure,
    decimal PlanQuantity,
    decimal FactQuantity,
    decimal RemainingQuantity,
    string? Comment);

public sealed record MobileShippingOrderMovementResponse(
    Guid Id,
    int LineNumber,
    Guid StockKeepingUnitId,
    decimal Quantity,
    MobileShippingOrderLocationResponse Source,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    DateTimeOffset? PostedAtUtc);

public sealed record MobileShippingOrderDetailsResponse(
    MobileShippingOrderSummaryResponse Order,
    IReadOnlyList<MobileShippingOrderLineResponse> Lines,
    IReadOnlyList<MobileShippingOrderMovementResponse> Movements);

public sealed record MobileShippingOrderWorkQueueResponse(
    IReadOnlyList<MobileShippingOrderSummaryResponse> Picking,
    IReadOnlyList<MobileShippingOrderSummaryResponse> Shipping);

public sealed record MobileShippingOrderLineCandidateResponse(
    int LineNumber,
    Guid StockKeepingUnitId,
    string SkuCode,
    string SkuName,
    string? UnitOfMeasure,
    decimal PlanQuantity,
    decimal FactQuantity,
    decimal RemainingQuantity,
    bool IsExactMatch);

public sealed record MobileShippingOrderLineSearchResponse(
    IReadOnlyList<MobileShippingOrderLineCandidateResponse> Items,
    bool HasMore);

public sealed record MobileShippingOrderSourceAvailabilityResponse(
    MobileShippingOrderLocationResponse Source,
    decimal PhysicalQuantity,
    decimal DraftQuantity,
    decimal AvailableQuantity);

public sealed record MobileResolveShippingOrderDocumentRequest(
    Guid WarehouseId,
    string Barcode);

public sealed record MobileResolveShippingOrderSkuRequest(string Barcode);

public sealed record MobileStartShippingOrderPickingRequest(
    Guid ClientRequestId,
    string ShippingLocationBarcode);

public sealed record MobileAddShippingOrderPickingMovementRequest(
    Guid ClientRequestId,
    int LineNumber,
    string SourceStorageLocationBarcode,
    decimal Quantity);

public sealed record MobileShippingOrderCommandRequest(Guid ClientRequestId);

public sealed record MobileShippingOrderCommandResponse(
    MobileShippingOrderDetailsResponse Details,
    int? ChangedLineNumber = null,
    Guid? ChangedMovementId = null);


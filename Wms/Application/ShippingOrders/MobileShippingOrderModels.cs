using Wms.Domain.Enums;

namespace Wms.Application.ShippingOrders;

public sealed record MobileShippingOrderLocation(
    Guid Id,
    string Name,
    string Address,
    Guid ZoneId,
    string ZoneName);

public sealed record MobileShippingOrderSummary(
    Guid Id,
    string Number,
    DateTime Date,
    Guid WarehouseId,
    string WarehouseName,
    Guid ReceiverId,
    PartyType ReceiverType,
    string ReceiverName,
    ShippingOrderQueue Queue,
    WarehouseOperation WarehouseOperation,
    ShippingOrderStatus Status,
    string? Comment,
    DateTime? PlannedShippingDate,
    string? DeliveryDirection,
    MobileShippingOrderLocation? ShippingLocation,
    int TotalLineCount,
    int FullyPickedLineCount,
    int PartiallyPickedLineCount,
    int ZeroPickedLineCount,
    double PlanQuantity,
    double FactQuantity,
    DateTimeOffset? PickingStartedAtUtc,
    DateTimeOffset? ReadyForShipmentAtUtc,
    DateTimeOffset? ShippedAtUtc);

public sealed record MobileShippingOrderLine(
    int LineNumber,
    Guid StockKeepingUnitId,
    string SkuCode,
    string SkuName,
    string? UnitOfMeasure,
    double PlanQuantity,
    double FactQuantity,
    string? Comment);

public sealed record MobileShippingOrderMovement(
    Guid Id,
    int LineNumber,
    Guid StockKeepingUnitId,
    double Quantity,
    MobileShippingOrderLocation Source,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    DateTimeOffset? PostedAtUtc);

public sealed record MobileShippingOrderDetails(
    MobileShippingOrderSummary Order,
    IReadOnlyList<MobileShippingOrderLine> Lines,
    IReadOnlyList<MobileShippingOrderMovement> Movements);

public sealed record MobileShippingOrderWorkQueue(
    IReadOnlyList<MobileShippingOrderSummary> Picking,
    IReadOnlyList<MobileShippingOrderSummary> Shipping);

public sealed record MobileShippingOrderLineCandidate(
    int LineNumber,
    Guid StockKeepingUnitId,
    string SkuCode,
    string SkuName,
    string? UnitOfMeasure,
    double PlanQuantity,
    double FactQuantity,
    bool IsExactMatch);

public sealed record MobileShippingOrderLineSearchResult(
    IReadOnlyList<MobileShippingOrderLineCandidate> Items,
    bool HasMore);

public sealed record MobileShippingOrderSourceAvailability(
    MobileShippingOrderLocation Source,
    double PhysicalQuantity,
    double DraftQuantity);

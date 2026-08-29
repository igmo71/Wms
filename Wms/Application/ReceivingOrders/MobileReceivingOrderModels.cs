using Wms.Domain.Enums;

namespace Wms.Application.ReceivingOrders;

public enum ReceivingOrderLineContext
{
    Receiving = 1,
    Putaway = 2
}

public sealed record MobileReceivingOrderLocation(
    Guid Id,
    string Name,
    string Address,
    Guid ZoneId,
    string ZoneName);

public sealed record MobileReceivingOrderSummary(
    Guid Id,
    string Number,
    DateTime Date,
    Guid WarehouseId,
    string WarehouseName,
    Guid ShipperId,
    PartyType ShipperType,
    string ShipperName,
    ReceivingOrderQueue Queue,
    WarehouseOperation WarehouseOperation,
    BusinessOperation BusinessOperation,
    ReceivingOrderStatus Status,
    PutawayStatus PutawayStatus,
    string? Comment,
    MobileReceivingOrderLocation? ReceivingLocation,
    int TotalLineCount,
    int ConfirmedLineCount,
    int PositiveLineCount,
    int FullyAllocatedLineCount,
    double PlanQuantity,
    double FactQuantity,
    double AllocatedQuantity,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    DateTimeOffset? PutawayStartedAtUtc,
    DateTimeOffset? PutawayCompletedAtUtc);

public sealed record MobileReceivingOrderLine(
    int LineNumber,
    Guid StockKeepingUnitId,
    string SkuCode,
    string SkuName,
    string? UnitOfMeasure,
    double PlanQuantity,
    double? FactQuantity,
    double AllocatedQuantity,
    double? RemainingPutawayQuantity,
    string? Comment);

public sealed record MobileReceivingOrderMovement(
    Guid Id,
    int LineNumber,
    Guid StockKeepingUnitId,
    double Quantity,
    MobileReceivingOrderLocation Destination,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    DateTimeOffset? PostedAtUtc);

public sealed record MobileReceivingOrderDetails(
    MobileReceivingOrderSummary Order,
    IReadOnlyList<MobileReceivingOrderLine> Lines,
    IReadOnlyList<MobileReceivingOrderMovement> Movements);

public sealed record MobileReceivingOrderWorkQueue(
    IReadOnlyList<MobileReceivingOrderSummary> Receiving,
    IReadOnlyList<MobileReceivingOrderSummary> Putaway);

public sealed record MobileReceivingOrderLineCandidate(
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

public sealed record MobileReceivingOrderLineSearchResult(
    IReadOnlyList<MobileReceivingOrderLineCandidate> Items,
    bool HasMore);

using Wms.Domain.Enums;

namespace Wms.Application.ReceivingOrders;

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
    OrderSynchronizationLevel SynchronizationLevel,
    string? Comment,
    MobileReceivingOrderLocation? ReceivingLocation,
    int TotalLineCount,
    int ConfirmedLineCount,
    int PositiveLineCount,
    decimal PlanQuantity,
    decimal FactQuantity,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    bool IsParticipant,
    bool CanJoin,
    string? JoinBlockedReason);

public sealed record MobileReceivingOrderLine(
    int LineNumber,
    Guid StockKeepingUnitId,
    string SkuCode,
    string SkuName,
    string? UnitOfMeasure,
    decimal PlanQuantity,
    decimal? FactQuantity,
    string? Comment);

public sealed record MobileReceivingOrderLicensePlateNumber(
    Guid Id,
    string Code,
    LicensePlateNumberStatus Status,
    string ResponsibleUserId,
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
    IReadOnlyList<MobileReceivingOrderLicensePlateNumberItem> Items);

public sealed record MobileReceivingOrderLicensePlateNumberItem(
    Guid StockKeepingUnitId,
    string SkuCode,
    string SkuName,
    string? UnitOfMeasure,
    decimal Quantity,
    string CreatedByName,
    DateTimeOffset CreatedAtUtc,
    string? UpdatedByName,
    DateTimeOffset? UpdatedAtUtc);

public sealed record MobileReceivingOrderDetails(
    MobileReceivingOrderSummary Order,
    IReadOnlyList<MobileReceivingOrderLine> Lines,
    IReadOnlyList<MobileReceivingOrderLicensePlateNumber> LicensePlateNumbers);

public sealed record MobileReceivingOrderWorkQueue(
    IReadOnlyList<MobileReceivingOrderSummary> Personal,
    IReadOnlyList<MobileReceivingOrderSummary> Available);

public sealed record MobileReceivingOrderLineCandidate(
    int LineNumber,
    Guid StockKeepingUnitId,
    string SkuCode,
    string SkuName,
    string? UnitOfMeasure,
    decimal PlanQuantity,
    decimal? FactQuantity,
    bool IsExactMatch);

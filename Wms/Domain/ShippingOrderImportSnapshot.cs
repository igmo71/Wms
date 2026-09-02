using Wms.Domain.Enums;

namespace Wms.Domain;

public sealed record ShippingOrderImportSnapshot(
    Guid Id,
    bool DeletionMark,
    bool Posted,
    string? Number,
    DateTime Date,
    Guid WarehouseId,
    string? Comment,
    ShippingOrderStatus Status,
    ShippingOrderQueue Queue,
    DateTime? PlannedShippingDate,
    Guid? DeliveryDirectionId,
    WarehouseOperation WarehouseOperation,
    Guid ReceiverId,
    PartyType ReceiverType,
    IReadOnlyCollection<ShippingOrderItemImportSnapshot> Items,
    IReadOnlyCollection<ShippingOrderBaseItemImportSnapshot> BaseItems);

public sealed record ShippingOrderItemImportSnapshot(
    int LineNumber,
    Guid StockKeepingUnitId,
    decimal PlanQuantity,
    decimal Quantity,
    ShippingOrderAction Action);

public sealed record ShippingOrderBaseItemImportSnapshot(
    int LineNumber,
    Guid StockKeepingUnitId,
    decimal PlanQuantity,
    Guid BaseOrderId,
    string? BaseOrderType);

public enum ShippingOrderReconciliation
{
    Unchanged,
    Updated,
    Conflict
}

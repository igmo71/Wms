using Wms.Domain.Enums;

namespace Wms.Domain;

public sealed record ReceivingOrderImportSnapshot(
    Guid Id,
    bool DeletionMark,
    bool Posted,
    string? Number,
    DateTime Date,
    Guid WarehouseId,
    string? Comment,
    ReceivingOrderStatus Status,
    ReceivingOrderQueue Queue,
    WarehouseOperation WarehouseOperation,
    BusinessOperation BusinessOperation,
    Guid ShipperId,
    PartyType ShipperType,
    Guid BaseOrderId,
    string? BaseOrderType,
    IReadOnlyCollection<ReceivingOrderItemImportSnapshot> Items);

public sealed record ReceivingOrderItemImportSnapshot(
    int LineNumber,
    Guid StockKeepingUnitId,
    double PlanQuantity);

public enum ReceivingOrderReconciliation
{
    Unchanged,
    Updated,
    Conflict
}

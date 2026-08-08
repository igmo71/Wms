using Wms.Domain.Enums;

namespace Wms.Domain;

public class ShippingOrder
{
    public Guid Id { get; set; }
    public bool DeletionMark { get; set; }
    public bool Posted { get; set; }
    public string? Number { get; set; }
    public DateTime Date { get; set; }

    public Guid WarehouseId { get; set; }
    public Warehouse? Warehouse { get; set; }

    public Guid? ShippingLocationId { get; set; }
    public StorageLocation? ShippingLocation { get; set; }

    public string? Comment { get; set; }

    public ReceivingOrderStatus Status { get; set; }
    public ReceivingOrderQueue Queue { get; set; }
    public DateTime PlannedShippingDate { get; set; }
    public Guid? DeliveryDirectionId { get; set; }
    public DeliveryDirection? DeliveryDirection { get; set; }
    public WarehouseOperation WarehouseOperation { get; set; }


    public DateTimeOffset? CreatedAtUtc { get; set; }
    public DateTimeOffset? UpdatedAtUtc { get; set; }
    public DateTimeOffset? StartedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }

    public string? StartedBy { get; set; }
    public string? CompletedBy { get; set; }

    public bool ExternalChangeDetected { get; set; }

    public Guid RecipientId { get; set; }
    public string? RecipientType { get; set; }

    public List<ShippingOrderBaseItem> BaseOrderItems { get; set; } = [];
    public List<ShippingOrderItem> ShippingItems { get; set; } = [];
}

using Wms.Domain.Enums;

namespace Wms.Domain;

public class ReceivingOrder
{
    public Guid Id { get; set; }
    public string? DataVersion { get; set; }
    public bool Posted { get; set; }
    public bool DeletionMark { get; set; }
    public DateTime DateTime { get; set; }
    public string? Number { get; set; }
    public string? Comment { get; set; }
    public Guid? WarehouseId { get; set; }
    public Warehouse? Warehouse { get; set; }

    public Guid? ReceivingLocationId { get; set; }
    public StorageLocation? ReceivingLocation { get; set; }
    public ReceivingOrderStatus Status { get; set; }
    public ReceivingOrderQueue Queue { get; set; }
    public WarehouseOperation WarehouseOperation { get; set; }
    public BusinessOperation BusinessOperation { get; set; }



    public DateTimeOffset? StartedAtUtc { get; set; }

    public DateTimeOffset? CompletedAtUtc { get; set; }

    public Guid? SenderId { get; set; }
    public string? SenderType { get; set; }

    public Guid? BaseOrderId { get; set; }
    public string? BaseOrderType { get; set; }

    public List<ReceivingOrderItem> Items { get; set; } = [];

    public bool IsFullyReceived => Items.All(x => x.IsFullyReceived);

    public bool HasPlanFactDifference => Items.Any(x => x.IsPlanFactDifference);
}

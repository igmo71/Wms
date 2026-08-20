using Wms.Common;

namespace Wms.Application.ReceivingOrders;

using Wms.Domain.Enums;

public class ReceivingOrderListQuery : ListQuery
{
    public Guid? WarehouseId { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public ReceivingOrderStatus? Status { get; set; }
    public PutawayStatus? PutawayStatus { get; set; }
    public ReceivingOrderQueue? Queue { get; set; }
    public WarehouseOperation? WarehouseOperation { get; set; }
    public bool IncludePostedOnly { get; set; } = true;
}

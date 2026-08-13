using Wms.Common;

namespace Wms.Application.Services.ReceivingOrders;

using Wms.Domain.Enums;

public class ReceivingOrderListQuery : ListQuery
{
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public ReceivingOrderStatus? Status { get; set; }
    public ReceivingOrderQueue? Queue { get; set; }
    public bool IncludePostedOnly { get; set; } = true;
}

using Wms.Common;

namespace Wms.Application.Services;

using Wms.Domain.Enums;

public class ShippingOrderListQuery : ListQuery
{
    public Guid? WarehouseId { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public ShippingOrderStatus? Status { get; set; }
    public ShippingOrderQueue? Queue { get; set; }
    public bool IncludePostedOnly { get; set; } = true;
}

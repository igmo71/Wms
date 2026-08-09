using Wms.Common;

namespace Wms.Application.Services;

using Wms.Domain.Enums;

public class ReceivingOrderListQuery : ListQuery
{
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public ReceivingOrderStatus? Status { get; set; }
    public bool IncludePostedOnly { get; set; } = true;
}

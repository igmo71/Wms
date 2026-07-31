namespace Wms.Common;

using Wms.Domain.Enums;

public class DocumentListQuery : ListQuery
{
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public ReceivingOrderStatus? Status { get; set; }
}

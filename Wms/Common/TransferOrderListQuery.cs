using Wms.Domain.Enums;

namespace Wms.Common;

public class TransferOrderListQuery : ListQuery
{
    public Guid? WarehouseId { get; set; }
    public TransferOrderStatus? Status { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
}

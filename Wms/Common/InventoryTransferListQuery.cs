using Wms.Domain.Enums;

namespace Wms.Common;

public class InventoryTransferListQuery : ListQuery
{
    public Guid? WarehouseId { get; set; }
    public InventoryTransferStatus? Status { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
}

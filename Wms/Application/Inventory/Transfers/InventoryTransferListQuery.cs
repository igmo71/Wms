using Wms.Common;
using Wms.Domain.Enums;

namespace Wms.Application.Inventory.Transfers;

public class InventoryTransferListQuery : ListQuery
{
    public Guid? WarehouseId { get; set; }
    public InventoryTransferStatus? Status { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
}

using Wms.Common;

namespace Wms.Application.Inventory.Balances;

public class InventoryBalanceListQuery : ListQuery
{
    public Guid? WarehouseId { get; set; }
    public Guid? StorageLocationId { get; set; }
    public Guid? StockKeepingUnitId { get; set; }
}

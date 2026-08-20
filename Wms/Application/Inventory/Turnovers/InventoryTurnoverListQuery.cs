using Wms.Common;

namespace Wms.Application.Inventory.Turnovers;

public class InventoryTurnoverListQuery : ListQuery
{
    public string? DocumentSearchString { get; set; }
    public Guid? WarehouseId { get; set; }
    public Guid? StorageLocationId { get; set; }
    public Guid? StockKeepingUnitId { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
}

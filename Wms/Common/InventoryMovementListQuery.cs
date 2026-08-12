namespace Wms.Common;

public class InventoryMovementListQuery : ListQuery
{
    public string? DocumentSearchString { get; set; }
    public Guid? WarehouseId { get; set; }
    public Guid? StorageLocationId { get; set; }
    public Guid? StockKeepingUnitId { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
}

using Wms.WebApp.Abstractions;

namespace Wms.WebApp.Domain;

public class Balance : EntityBase
{
    public decimal Quantity { get; set; }

    public Guid StockKeepingUnitId { get; private set; }
    public StockKeepingUnit? StockKeepingUnit { get; private set; }

    public Guid WarehouseId { get; private set; }
    public Warehouse? Warehouse { get; private set; }

    public Guid StorageLocationId { get; private set; }
    public StorageLocation? StorageLocation { get; private set; }
}

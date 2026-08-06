using Wms.Domain.Enums;

namespace Wms.Domain;

public class InventoryTurnover
{
    public Guid Id { get; set; }

    public Guid WarehouseId { get; set; }
    public Warehouse? Warehouse { get; set; }

    public Guid StorageLocationId { get; set; }
    public StorageLocation? StorageLocation { get; set; }

    public Guid StockKeepingUnitId { get; set; }
    public StockKeepingUnit? StockKeepingUnit { get; set; }

    public double QuantityDelta { get; set; }
    public double BalanceBefore { get; set; }
    public double BalanceAfter { get; set; }

    public DateTimeOffset DateTimeUtc { get; set; }

    public Guid? RecorderId { get; set; }
    public int RecorderLineNumber { get; set; }
    public RecorderType RecorderType { get; set; }
}

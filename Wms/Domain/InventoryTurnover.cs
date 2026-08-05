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

    public decimal QuantityDelta { get; set; }
    public decimal BalanceBefore { get; set; }
    public decimal BalanceAfter { get; set; }

    public DateTimeOffset DateTimeUtc { get; set; }

    public Guid? RecorderId { get; set; }
    public int RecorderLineNumber { get; set; }
    public RecorderType RecorderType { get; set; }
}

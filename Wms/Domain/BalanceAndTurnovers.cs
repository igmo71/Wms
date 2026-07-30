namespace Wms.Domain;

public class BalanceAndTurnovers
{
    public Guid Id { get; set; }

    public DateTime DateTime { get; set; }

    public Guid StockKeepingUnitId { get; private set; }
    public StockKeepingUnit? StockKeepingUnit { get; private set; }

    public Guid WarehouseId { get; private set; }
    public Warehouse? Warehouse { get; private set; }

    public Guid StorageLocationId { get; private set; }
    public StorageLocation? StorageLocation { get; private set; }

    public Guid? RecorderId { get; set; }
    public string? RecorderType { get; set; }
    public int LineNumber { get; set; }

    public decimal QuantityDelta { get; private set; }
    public decimal BalanceBefore { get; private set; }
    public decimal BalanceAfter { get; private set; }
}

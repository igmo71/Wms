namespace Wms.Domain;

public class BalanceAndTurnovers
{
    public Guid Id { get; set; }
    public decimal OpeningBalance { get; set; }
    public decimal Turnover { get; set; }
    public decimal Receipt { get; set; }
    public decimal Expense { get; set; }
    public decimal ClosingBalance { get; set; }

    public Guid StockKeepingUnitId { get; private set; }
    public StockKeepingUnit? StockKeepingUnit { get; private set; }

    public Guid WarehouseId { get; private set; }
    public Warehouse? Warehouse { get; private set; }

    public Guid StorageLocationId { get; private set; }
    public StorageLocation? StorageLocation { get; private set; }
}

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
    public double? WeightDeltaKg => WeightCalculation.CalculateKg(QuantityDelta, StockKeepingUnit);
    public double? WeightBeforeKg => WeightCalculation.CalculateKg(BalanceBefore, StockKeepingUnit);
    public double? WeightAfterKg => WeightCalculation.CalculateKg(BalanceAfter, StockKeepingUnit);

    public DateTimeOffset CreatedAtUtc { get; set; }

    public Guid InventoryMovementId { get; set; }
    public InventoryMovement? InventoryMovement { get; set; }
}

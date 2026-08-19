using Wms.Common;

namespace Wms.Domain;

public class InventoryTurnover
{
    private InventoryTurnover()
    {
    }

    public Guid Id { get; private set; }
    public Guid WarehouseId { get; private set; }
    public Warehouse? Warehouse { get; private set; }
    public Guid StorageLocationId { get; private set; }
    public StorageLocation? StorageLocation { get; private set; }
    public Guid StockKeepingUnitId { get; private set; }
    public StockKeepingUnit? StockKeepingUnit { get; private set; }
    public double QuantityDelta { get; private set; }
    public double BalanceBefore { get; private set; }
    public double BalanceAfter { get; private set; }
    public double? WeightDeltaKg => WeightCalculation.CalculateKg(QuantityDelta, StockKeepingUnit);
    public double? WeightBeforeKg => WeightCalculation.CalculateKg(BalanceBefore, StockKeepingUnit);
    public double? WeightAfterKg => WeightCalculation.CalculateKg(BalanceAfter, StockKeepingUnit);
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public Guid InventoryMovementId { get; private set; }
    public InventoryMovement? InventoryMovement { get; private set; }

    public static OperationResult<InventoryTurnover> Create(
        Guid id,
        Guid warehouseId,
        Guid storageLocationId,
        Guid stockKeepingUnitId,
        InventoryBalanceChange change,
        DateTimeOffset createdAtUtc,
        Guid inventoryMovementId)
    {
        if (id == Guid.Empty
            || warehouseId == Guid.Empty
            || storageLocationId == Guid.Empty
            || stockKeepingUnitId == Guid.Empty
            || inventoryMovementId == Guid.Empty)
        {
            return OperationError.Invalid<InventoryTurnover>("Inventory turnover identifiers are required.");
        }

        if (!double.IsFinite(change.BalanceBefore)
            || !double.IsFinite(change.QuantityDelta)
            || !double.IsFinite(change.BalanceAfter)
            || change.QuantityDelta == 0
            || change.BalanceBefore < 0
            || change.BalanceAfter < 0
            || change.BalanceBefore + change.QuantityDelta != change.BalanceAfter)
        {
            return OperationError.Invalid<InventoryTurnover>("Inventory turnover balance change is invalid.");
        }

        if (createdAtUtc == default)
        {
            return OperationError.Invalid<InventoryTurnover>("Inventory turnover creation time is required.");
        }

        return new InventoryTurnover
        {
            Id = id,
            WarehouseId = warehouseId,
            StorageLocationId = storageLocationId,
            StockKeepingUnitId = stockKeepingUnitId,
            QuantityDelta = change.QuantityDelta,
            BalanceBefore = change.BalanceBefore,
            BalanceAfter = change.BalanceAfter,
            CreatedAtUtc = createdAtUtc,
            InventoryMovementId = inventoryMovementId
        };
    }
}

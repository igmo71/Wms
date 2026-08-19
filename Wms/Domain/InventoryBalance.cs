using Wms.Common;

namespace Wms.Domain;

public class InventoryBalance
{
    private InventoryBalance()
    {
    }

    public Guid Id { get; private set; }
    public Guid WarehouseId { get; private set; }
    public Warehouse? Warehouse { get; private set; }
    public Guid StorageLocationId { get; private set; }
    public StorageLocation? StorageLocation { get; private set; }
    public Guid StockKeepingUnitId { get; private set; }
    public StockKeepingUnit? StockKeepingUnit { get; private set; }
    public double Quantity { get; private set; }
    public double? WeightKg => WeightCalculation.CalculateKg(Quantity, StockKeepingUnit);
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? UpdatedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public static OperationResult<InventoryBalance> Create(
        Guid id,
        Guid warehouseId,
        Guid storageLocationId,
        Guid stockKeepingUnitId,
        double quantity,
        DateTimeOffset createdAtUtc)
    {
        if (id == Guid.Empty
            || warehouseId == Guid.Empty
            || storageLocationId == Guid.Empty
            || stockKeepingUnitId == Guid.Empty)
        {
            return OperationError.Invalid<InventoryBalance>("Inventory balance identifiers are required.");
        }

        if (!double.IsFinite(quantity) || quantity < 0)
        {
            return OperationError.Invalid<InventoryBalance>(
                "Inventory balance quantity must be a finite non-negative number.");
        }

        if (createdAtUtc == default)
        {
            return OperationError.Invalid<InventoryBalance>("Inventory balance creation time is required.");
        }

        return new InventoryBalance
        {
            Id = id,
            WarehouseId = warehouseId,
            StorageLocationId = storageLocationId,
            StockKeepingUnitId = stockKeepingUnitId,
            Quantity = quantity,
            CreatedAtUtc = createdAtUtc
        };
    }

    public OperationResult<InventoryBalanceChange> Adjust(
        double quantityDelta,
        DateTimeOffset updatedAtUtc)
    {
        if (!double.IsFinite(quantityDelta) || quantityDelta == 0)
        {
            return OperationError.Invalid<InventoryBalance>(
                "Inventory balance change must be a finite non-zero number.");
        }

        if (updatedAtUtc == default || updatedAtUtc < CreatedAtUtc)
        {
            return OperationError.Invalid<InventoryBalance>(
                "Inventory balance update time cannot precede its creation time.");
        }

        var balanceAfter = Quantity + quantityDelta;
        if (!double.IsFinite(balanceAfter) || balanceAfter < 0)
        {
            return OperationError.Invalid<InventoryBalance>("Inventory balance cannot be negative.");
        }

        var change = new InventoryBalanceChange(Quantity, quantityDelta, balanceAfter);
        Quantity = balanceAfter;
        UpdatedAtUtc = updatedAtUtc;
        return change;
    }
}

public readonly record struct InventoryBalanceChange(
    double BalanceBefore,
    double QuantityDelta,
    double BalanceAfter);

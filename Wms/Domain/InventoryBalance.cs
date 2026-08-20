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
            return OperationError.Invalid("Идентификаторы складского остатка обязательны.");
        }

        if (!double.IsFinite(quantity) || quantity < 0)
        {
            return OperationError.Invalid(
                "Количество остатка должно быть конечным неотрицательным числом.");
        }

        if (createdAtUtc == default)
        {
            return OperationError.Invalid("Время создания складского остатка обязательно.");
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
            return OperationError.Invalid(
                "Изменение остатка должно быть конечным ненулевым числом.");
        }

        if (updatedAtUtc == default || updatedAtUtc < CreatedAtUtc)
        {
            return OperationError.Invalid(
                "Время изменения остатка не может предшествовать времени его создания.");
        }

        var balanceAfter = Quantity + quantityDelta;
        if (!double.IsFinite(balanceAfter) || balanceAfter < 0)
        {
            return OperationError.Invalid("Складской остаток не может быть отрицательным.");
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

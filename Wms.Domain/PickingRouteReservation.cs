using Wms.Common;

namespace Wms.Domain;

public sealed class PickingRouteReservation
{
    private PickingRouteReservation() { }

    public Guid Id { get; private set; }
    public Guid PickingAssemblyId { get; private set; }
    public Guid WarehouseId { get; private set; }
    public Guid StorageLocationId { get; private set; }
    public Guid StockKeepingUnitId { get; private set; }
    public DateOnly? ExpirationDate { get; private set; }
    public decimal Quantity { get; private set; }

    public OperationResult ChangeQuantity(decimal quantity)
    {
        if (!WarehouseQuantity.IsPositive(quantity))
            return OperationError.Invalid(
                "Закрепляемое количество должно быть положительным с точностью до 0,001.");
        Quantity = quantity;
        return OperationResult.Success();
    }

    public static OperationResult<PickingRouteReservation> Create(
        Guid assemblyId, Guid warehouseId, Guid locationId, Guid skuId,
        DateOnly? expirationDate, decimal quantity)
    {
        if (assemblyId == Guid.Empty || warehouseId == Guid.Empty
            || locationId == Guid.Empty || skuId == Guid.Empty)
            return OperationError.Invalid("Для закрепления нужны сборка, склад, ячейка и SKU.");
        if (!WarehouseQuantity.IsPositive(quantity))
            return OperationError.Invalid(
                "Закрепляемое количество должно быть положительным с точностью до 0,001.");

        return new PickingRouteReservation
        {
            Id = Guid.NewGuid(),
            PickingAssemblyId = assemblyId,
            WarehouseId = warehouseId,
            StorageLocationId = locationId,
            StockKeepingUnitId = skuId,
            ExpirationDate = expirationDate,
            Quantity = quantity
        };
    }
}

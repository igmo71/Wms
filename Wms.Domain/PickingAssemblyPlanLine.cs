using Wms.Common;

namespace Wms.Domain;

public sealed class PickingAssemblyPlanLine
{
    private PickingAssemblyPlanLine() { }

    public Guid PickingAssemblyId { get; private set; }
    public PickingAssembly? PickingAssembly { get; private set; }
    public Guid ShippingOrderId { get; private set; }
    public Guid StockKeepingUnitId { get; private set; }
    public StockKeepingUnit? StockKeepingUnit { get; private set; }
    public decimal Quantity { get; private set; }

    internal static OperationResult<PickingAssemblyPlanLine> Create(
        Guid assemblyId, Guid shippingOrderId, Guid stockKeepingUnitId, decimal quantity)
    {
        if (assemblyId == Guid.Empty || shippingOrderId == Guid.Empty
            || stockKeepingUnitId == Guid.Empty || !WarehouseQuantity.IsNonNegative(quantity))
            return OperationError.Invalid("Документ, SKU и количество плана должны быть допустимы.");

        return new PickingAssemblyPlanLine
        {
            PickingAssemblyId = assemblyId,
            ShippingOrderId = shippingOrderId,
            StockKeepingUnitId = stockKeepingUnitId,
            Quantity = quantity
        };
    }

    internal OperationResult SetQuantity(decimal quantity)
    {
        if (!WarehouseQuantity.IsNonNegative(quantity))
            return OperationError.Invalid("Количество плана должно быть неотрицательным с точностью до 0,001.");
        Quantity = quantity;
        return OperationResult.Success();
    }
}

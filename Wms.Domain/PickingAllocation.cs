using Wms.Common;

namespace Wms.Domain;

public sealed class PickingAllocation
{
    private PickingAllocation() { }

    public Guid PickingAssemblyId { get; private set; }
    public Guid ShippingOrderId { get; private set; }
    public Guid StockKeepingUnitId { get; private set; }
    public decimal Quantity { get; private set; }

    public static OperationResult<PickingAllocation> Create(Guid assemblyId,
        Guid shippingOrderId, Guid skuId, decimal quantity)
    {
        if (assemblyId == Guid.Empty || shippingOrderId == Guid.Empty
            || skuId == Guid.Empty || !WarehouseQuantity.IsNonNegative(quantity))
            return OperationError.Invalid("Распределение требует сборку, документ, SKU и допустимое количество.");
        return new PickingAllocation
        {
            PickingAssemblyId = assemblyId, ShippingOrderId = shippingOrderId,
            StockKeepingUnitId = skuId, Quantity = quantity
        };
    }
}

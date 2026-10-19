using Wms.Common;

namespace Wms.Domain;

public class ReceivingOrderItem
{
    private ReceivingOrderItem() { }

    public Guid ReceivingOrderId { get; private set; }
    public ReceivingOrder? ReceivingOrder { get; private set; }
    public Guid StockKeepingUnitId { get; private set; }
    public StockKeepingUnit? StockKeepingUnit { get; private set; }
    public decimal? CurrentPlanQuantity { get; private set; }
    public decimal? FactQuantity { get; private set; }

    public static OperationResult<ReceivingOrderItem> CreateForSku(
        Guid receivingOrderId, Guid stockKeepingUnitId, decimal? currentPlanQuantity)
    {
        if (receivingOrderId == Guid.Empty || stockKeepingUnitId == Guid.Empty
            || currentPlanQuantity is decimal plan && !WarehouseQuantity.IsNonNegative(plan))
            return OperationError.Invalid("Ордер, SKU или плановое количество недопустимы.");

        return new ReceivingOrderItem
        {
            ReceivingOrderId = receivingOrderId,
            StockKeepingUnitId = stockKeepingUnitId,
            CurrentPlanQuantity = currentPlanQuantity
        };
    }

    public OperationResult SetCurrentPlan(decimal? currentPlanQuantity)
    {
        if (currentPlanQuantity is decimal plan && !WarehouseQuantity.IsNonNegative(plan))
            return OperationError.Invalid("Плановое количество недопустимо.");
        CurrentPlanQuantity = currentPlanQuantity;
        return OperationResult.Success();
    }

    internal void SetAggregatedLpnFact(decimal? factQuantity) => FactQuantity = factQuantity;
}

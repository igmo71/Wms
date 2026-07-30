namespace Wms.Domain;

public class ReceivingOrderItem
{
    public Guid ReceivingOrderId { get; set; }
    public ReceivingOrder? ReceivingOrder { get; set; }

    public int LineNumber { get; set; }

    public Guid? StockKeepingUnitId { get; set; }
    public StockKeepingUnit? StockKeepingUnit { get; set; }

    public double PlanQuantity { get; set; }
    public double FactQuantity { get; set; }

    public double RemainingQuantity => PlanQuantity - FactQuantity;
    public bool IsFullyReceived => FactQuantity == PlanQuantity;
}

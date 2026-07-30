namespace Wms.Domain;

public class ReceivingOrderItem
{
    public Guid ReceivingOrderId { get; set; }
    public ReceivingOrder? ReceivingOrder { get; set; }

    public int LineNumber { get; set; }

    public Guid? StockKeepingUnitId { get; set; }
    public StockKeepingUnit? StockKeepingUnit { get; set; }

    public double PlannQuantity { get; set; }
    public double FactQuantity { get; set; }

    public double RemainingQuantity => PlannQuantity - FactQuantity;
    public bool IsFullyReceived => FactQuantity == PlannQuantity;
}

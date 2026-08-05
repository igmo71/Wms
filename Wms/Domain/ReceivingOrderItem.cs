namespace Wms.Domain;

public class ReceivingOrderItem
{
    public Guid ReceivingOrderId { get; set; }
    public ReceivingOrder? ReceivingOrder { get; set; }

    public int LineNumber { get; set; }

    public Guid StockKeepingUnitId { get; set; }
    public StockKeepingUnit? StockKeepingUnit { get; set; }

    public decimal PlanQuantity { get; set; }
    public decimal FactQuantity { get; set; }

    public string? Comment { get; set; }

    public decimal RemainingQuantity => PlanQuantity - FactQuantity;
    public bool IsFullyReceived => FactQuantity == PlanQuantity;
    public bool IsPlanFactDifference => FactQuantity != PlanQuantity;
}

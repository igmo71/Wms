namespace Wms.Domain;

public class ReceivingOrderItem
{
    public Guid ReceivingOrderId { get; set; }
    public ReceivingOrder? ReceivingOrder { get; set; }

    public int LineNumber { get; set; }

    public Guid StockKeepingUnitId { get; set; }
    public StockKeepingUnit? StockKeepingUnit { get; set; }

    public double PlanQuantity { get; set; }
    public double FactQuantity { get; set; }

    public string? Comment { get; set; }

    public double RemainingQuantity => PlanQuantity - FactQuantity;
    public double? FactWeightKg => WeightCalculation.CalculateKg(FactQuantity, StockKeepingUnit);
    public bool IsFullyReceived => FactQuantity == PlanQuantity;
    public bool IsPlanFactDifference => FactQuantity != PlanQuantity;
}

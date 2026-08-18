using Wms.Common;

namespace Wms.Domain;

public class ShippingOrderItem
{
    public Guid ShippingOrderId { get; set; }
    public ShippingOrder? ShippingOrder { get; set; }

    public int LineNumber { get; set; }

    public Guid StockKeepingUnitId { get; set; }
    public StockKeepingUnit? StockKeepingUnit { get; set; }

    public double PlanQuantity { get; set; }
    public double FactQuantity { get; set; }

    public string? Comment { get; set; }

    public double RemainingQuantity => PlanQuantity - FactQuantity;
    public double? FactWeightKg => WeightCalculation.CalculateKg(FactQuantity, StockKeepingUnit);
    public bool IsFullyShipped => FactQuantity == PlanQuantity;
}

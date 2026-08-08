namespace Wms.Domain;

public class ShippingOrderBaseItem
{
    public Guid ShippingOrderId { get; set; }
    public ShippingOrder? ShippingOrder { get; set; }

    public int LineNumber { get; set; }

    public Guid StockKeepingUnitId { get; set; }
    public StockKeepingUnit? StockKeepingUnit { get; set; }

    public double PlanQuantity { get; set; }

    public Guid BaseOrderId { get; set; }
    public string? BaseOrderType { get; set; }
}

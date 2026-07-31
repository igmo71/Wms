using Wms.Domain;

namespace Wms.Application;

public class ReceivingOrderItemDetails
{
    public Guid ReceivingOrderId { get; set; }
    public int LineNumber { get; set; }

    public string? StockKeepingUnitName { get; set; }

    public double PlanQuantity { get; set; }
    public double FactQuantity { get; set; }

    public string? Comment { get; set; }

    public double RemainingQuantity => PlanQuantity - FactQuantity;
    public bool IsFullyReceived => FactQuantity == PlanQuantity;
    public bool IsPlanFactDifference => FactQuantity != PlanQuantity;

    public static ReceivingOrderItemDetails From(ReceivingOrderItem item) =>
        new()
        {
            ReceivingOrderId = item.ReceivingOrderId,
            LineNumber = item.LineNumber,
            // TODO: ...
        };
}

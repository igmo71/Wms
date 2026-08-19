using Wms.Common;

namespace Wms.Domain;

public class ShippingOrderItem
{
    private ShippingOrderItem()
    {
    }

    public Guid ShippingOrderId { get; private set; }
    public ShippingOrder? ShippingOrder { get; private set; }
    public int LineNumber { get; private set; }
    public Guid StockKeepingUnitId { get; private set; }
    public StockKeepingUnit? StockKeepingUnit { get; private set; }
    public double PlanQuantity { get; private set; }
    public double FactQuantity { get; private set; }
    public string? Comment { get; private set; }

    public double RemainingQuantity => PlanQuantity - FactQuantity;
    public double? FactWeightKg => WeightCalculation.CalculateKg(FactQuantity, StockKeepingUnit);
    public bool IsFullyShipped => FactQuantity == PlanQuantity;

    internal static OperationResult<ShippingOrderItem> Create(
        Guid shippingOrderId,
        ShippingOrderItemImportSnapshot snapshot)
    {
        var validationResult = ValidateImport(shippingOrderId, snapshot);
        if (!validationResult.IsSuccess)
        {
            return validationResult.Error!;
        }

        return new ShippingOrderItem
        {
            ShippingOrderId = shippingOrderId,
            LineNumber = snapshot.LineNumber,
            StockKeepingUnitId = snapshot.StockKeepingUnitId,
            PlanQuantity = snapshot.PlanQuantity
        };
    }

    internal OperationResult Reconcile(ShippingOrderItemImportSnapshot snapshot)
    {
        var validationResult = ValidateImport(ShippingOrderId, snapshot);
        if (!validationResult.IsSuccess)
        {
            return validationResult;
        }

        if (snapshot.LineNumber != LineNumber)
        {
            return OperationError.Invalid<ShippingOrderItem>(
                "Shipping order item line number cannot be changed.");
        }

        StockKeepingUnitId = snapshot.StockKeepingUnitId;
        PlanQuantity = snapshot.PlanQuantity;
        return OperationResult.Success();
    }

    internal OperationResult UpdateFact(double factQuantity)
    {
        if (!double.IsFinite(factQuantity) || factQuantity < 0 || factQuantity > PlanQuantity)
        {
            return OperationError.Invalid<ShippingOrderItem>(
                "Fact quantity must be finite and between zero and the planned quantity.");
        }

        FactQuantity = factQuantity;
        return OperationResult.Success();
    }

    internal void ResetFact()
    {
        FactQuantity = 0;
    }

    internal static OperationResult ValidateImport(
        Guid shippingOrderId,
        ShippingOrderItemImportSnapshot snapshot)
    {
        if (shippingOrderId == Guid.Empty)
        {
            return OperationError.Invalid<ShippingOrder>("Shipping order identifier is required.");
        }

        if (snapshot.LineNumber <= 0)
        {
            return OperationError.Invalid<ShippingOrderItem>(
                "Shipping order item line number must be positive.");
        }

        if (snapshot.StockKeepingUnitId == Guid.Empty)
        {
            return OperationError.Invalid<StockKeepingUnit>("SKU identifier is required.");
        }

        if (!double.IsFinite(snapshot.PlanQuantity) || snapshot.PlanQuantity < 0)
        {
            return OperationError.Invalid<ShippingOrderItem>(
                "Planned quantity must be a finite non-negative number.");
        }

        return OperationResult.Success();
    }
}

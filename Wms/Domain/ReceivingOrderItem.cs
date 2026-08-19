using Wms.Common;

namespace Wms.Domain;

public class ReceivingOrderItem
{
    private ReceivingOrderItem()
    {
    }

    public Guid ReceivingOrderId { get; private set; }
    public ReceivingOrder? ReceivingOrder { get; private set; }
    public int LineNumber { get; private set; }
    public Guid StockKeepingUnitId { get; private set; }
    public StockKeepingUnit? StockKeepingUnit { get; private set; }
    public double PlanQuantity { get; private set; }
    public double FactQuantity { get; private set; }
    public string? Comment { get; private set; }

    public double RemainingQuantity => PlanQuantity - FactQuantity;
    public double? FactWeightKg => WeightCalculation.CalculateKg(FactQuantity, StockKeepingUnit);
    public bool IsFullyReceived => FactQuantity == PlanQuantity;
    public bool IsPlanFactDifference => FactQuantity != PlanQuantity;

    internal static OperationResult<ReceivingOrderItem> Create(
        Guid receivingOrderId,
        ReceivingOrderItemImportSnapshot snapshot)
    {
        var validationResult = ValidateImport(receivingOrderId, snapshot);
        if (!validationResult.IsSuccess)
        {
            return validationResult.Error!;
        }

        return new ReceivingOrderItem
        {
            ReceivingOrderId = receivingOrderId,
            LineNumber = snapshot.LineNumber,
            StockKeepingUnitId = snapshot.StockKeepingUnitId,
            PlanQuantity = snapshot.PlanQuantity
        };
    }

    internal OperationResult Reconcile(ReceivingOrderItemImportSnapshot snapshot)
    {
        var validationResult = ValidateImport(ReceivingOrderId, snapshot);
        if (!validationResult.IsSuccess)
        {
            return validationResult;
        }

        if (snapshot.LineNumber != LineNumber)
        {
            return OperationError.Invalid<ReceivingOrderItem>(
                "Receiving order item line number cannot be changed.");
        }

        StockKeepingUnitId = snapshot.StockKeepingUnitId;
        PlanQuantity = snapshot.PlanQuantity;
        return OperationResult.Success();
    }

    internal OperationResult UpdateFact(double factQuantity, string? comment)
    {
        if (!double.IsFinite(factQuantity) || factQuantity < 0)
        {
            return OperationError.Invalid<ReceivingOrderItem>(
                "Fact quantity must be a finite non-negative number.");
        }

        FactQuantity = factQuantity;
        Comment = comment;
        return OperationResult.Success();
    }

    internal static OperationResult ValidateImport(
        Guid receivingOrderId,
        ReceivingOrderItemImportSnapshot snapshot)
    {
        if (receivingOrderId == Guid.Empty)
        {
            return OperationError.Invalid<ReceivingOrder>("Receiving order identifier is required.");
        }

        if (snapshot.LineNumber <= 0)
        {
            return OperationError.Invalid<ReceivingOrderItem>(
                "Receiving order item line number must be positive.");
        }

        if (snapshot.StockKeepingUnitId == Guid.Empty)
        {
            return OperationError.Invalid<StockKeepingUnit>("SKU identifier is required.");
        }

        if (!double.IsFinite(snapshot.PlanQuantity) || snapshot.PlanQuantity < 0)
        {
            return OperationError.Invalid<ReceivingOrderItem>(
                "Planned quantity must be a finite non-negative number.");
        }

        return OperationResult.Success();
    }
}

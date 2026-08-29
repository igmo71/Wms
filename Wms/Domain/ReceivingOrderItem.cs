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
    public double? FactQuantity { get; private set; }
    public string? Comment { get; private set; }

    public double? RemainingQuantity => FactQuantity is double factQuantity
        ? PlanQuantity - factQuantity
        : null;
    public double? FactWeightKg => FactQuantity is double factQuantity
        ? WeightCalculation.CalculateKg(factQuantity, StockKeepingUnit)
        : null;
    public bool IsFactConfirmed => FactQuantity.HasValue;
    public bool IsFullyReceived => FactQuantity == PlanQuantity;
    public bool IsPlanFactDifference => FactQuantity is double factQuantity
        && factQuantity != PlanQuantity;

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
            return OperationError.Invalid(
                "Номер строки приходного ордера нельзя изменить.");
        }

        StockKeepingUnitId = snapshot.StockKeepingUnitId;
        PlanQuantity = snapshot.PlanQuantity;
        return OperationResult.Success();
    }

    internal OperationResult UpdateFact(double factQuantity, string? comment)
    {
        if (!double.IsFinite(factQuantity) || factQuantity < 0)
        {
            return OperationError.Invalid(
                "Фактическое количество должно быть конечным неотрицательным числом.");
        }

        FactQuantity = factQuantity;
        Comment = comment;
        return OperationResult.Success();
    }

    internal OperationResult IncrementFact()
    {
        var factQuantity = (FactQuantity ?? 0) + 1;
        if (!double.IsFinite(factQuantity))
        {
            return OperationError.Invalid(
                "Фактическое количество должно быть конечным неотрицательным числом.");
        }

        FactQuantity = factQuantity;
        return OperationResult.Success();
    }

    internal void UpdateComment(string? comment)
    {
        Comment = comment;
    }

    internal static OperationResult ValidateImport(
        Guid receivingOrderId,
        ReceivingOrderItemImportSnapshot snapshot)
    {
        if (receivingOrderId == Guid.Empty)
        {
            return OperationError.Invalid("Идентификатор приходного ордера обязателен.");
        }

        if (snapshot.LineNumber <= 0)
        {
            return OperationError.Invalid(
                "Номер строки приходного ордера должен быть положительным.");
        }

        if (snapshot.StockKeepingUnitId == Guid.Empty)
        {
            return OperationError.Invalid("Идентификатор номенклатуры обязателен.");
        }

        if (!double.IsFinite(snapshot.PlanQuantity) || snapshot.PlanQuantity < 0)
        {
            return OperationError.Invalid(
                "Плановое количество должно быть конечным неотрицательным числом.");
        }

        return OperationResult.Success();
    }
}

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
    public decimal PlanQuantity { get; private set; }
    // New source projection keeps unknown distinct from a known zero. The legacy
    // PlanQuantity remains until its callers are replaced.
    public decimal? CurrentPlanQuantity { get; private set; }
    public decimal? FactQuantity { get; private set; }
    public string? Comment { get; private set; }

    public decimal? RemainingQuantity => FactQuantity is decimal factQuantity
        ? PlanQuantity - factQuantity
        : null;
    public double? FactWeightKg => FactQuantity is decimal factQuantity
        ? WeightCalculation.CalculateKg(factQuantity, StockKeepingUnit)
        : null;
    public bool IsFactConfirmed => FactQuantity.HasValue;

    public static OperationResult<ReceivingOrderItem> CreateForSku(
        Guid receivingOrderId, Guid stockKeepingUnitId, decimal? currentPlanQuantity)
    {
        if (receivingOrderId == Guid.Empty || stockKeepingUnitId == Guid.Empty
            || currentPlanQuantity is decimal plan && !WarehouseQuantity.IsNonNegative(plan))
            return OperationError.Invalid("Ордер, SKU или плановое количество недопустимы.");

        return new ReceivingOrderItem
        {
            ReceivingOrderId = receivingOrderId,
            StockKeepingUnitId = stockKeepingUnitId,
            CurrentPlanQuantity = currentPlanQuantity
        };
    }

    public OperationResult SetCurrentPlan(decimal? currentPlanQuantity)
    {
        if (currentPlanQuantity is decimal plan && !WarehouseQuantity.IsNonNegative(plan))
            return OperationError.Invalid("Плановое количество недопустимо.");
        CurrentPlanQuantity = currentPlanQuantity;
        return OperationResult.Success();
    }

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
            PlanQuantity = snapshot.PlanQuantity,
            CurrentPlanQuantity = snapshot.PlanQuantity
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
        CurrentPlanQuantity = snapshot.PlanQuantity;
        return OperationResult.Success();
    }

    internal void SetAggregatedLpnFact(decimal? factQuantity) => FactQuantity = factQuantity;

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

        if (!WarehouseQuantity.IsNonNegative(snapshot.PlanQuantity))
        {
            return OperationError.Invalid(
                "Плановое количество должно быть конечным неотрицательным числом.");
        }

        if (!WarehouseQuantity.IsNonNegative(snapshot.Quantity))
        {
            return OperationError.Invalid(
                "Количество в строке 1С должно быть конечным неотрицательным числом.");
        }

        return OperationResult.Success();
    }
}

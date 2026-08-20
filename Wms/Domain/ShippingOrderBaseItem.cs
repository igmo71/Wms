using Wms.Common;

namespace Wms.Domain;

public class ShippingOrderBaseItem
{
    private ShippingOrderBaseItem()
    {
    }

    public Guid ShippingOrderId { get; private set; }
    public ShippingOrder? ShippingOrder { get; private set; }
    public int LineNumber { get; private set; }
    public Guid StockKeepingUnitId { get; private set; }
    public StockKeepingUnit? StockKeepingUnit { get; private set; }
    public double PlanQuantity { get; private set; }
    public Guid BaseOrderId { get; private set; }
    public string? BaseOrderType { get; private set; }

    internal static OperationResult<ShippingOrderBaseItem> Create(
        Guid shippingOrderId,
        ShippingOrderBaseItemImportSnapshot snapshot)
    {
        var validationResult = ValidateImport(shippingOrderId, snapshot);
        if (!validationResult.IsSuccess)
        {
            return validationResult.Error!;
        }

        return new ShippingOrderBaseItem
        {
            ShippingOrderId = shippingOrderId,
            LineNumber = snapshot.LineNumber,
            StockKeepingUnitId = snapshot.StockKeepingUnitId,
            PlanQuantity = snapshot.PlanQuantity,
            BaseOrderId = snapshot.BaseOrderId,
            BaseOrderType = snapshot.BaseOrderType
        };
    }

    internal OperationResult Reconcile(ShippingOrderBaseItemImportSnapshot snapshot)
    {
        var validationResult = ValidateImport(ShippingOrderId, snapshot);
        if (!validationResult.IsSuccess)
        {
            return validationResult;
        }

        if (snapshot.LineNumber != LineNumber)
        {
            return OperationError.Invalid(
                "Номер строки основания расходного ордера нельзя изменить.");
        }

        StockKeepingUnitId = snapshot.StockKeepingUnitId;
        PlanQuantity = snapshot.PlanQuantity;
        BaseOrderId = snapshot.BaseOrderId;
        BaseOrderType = snapshot.BaseOrderType;
        return OperationResult.Success();
    }

    internal static OperationResult ValidateImport(
        Guid shippingOrderId,
        ShippingOrderBaseItemImportSnapshot snapshot)
    {
        if (shippingOrderId == Guid.Empty)
        {
            return OperationError.Invalid("Идентификатор расходного ордера обязателен.");
        }

        if (snapshot.LineNumber <= 0)
        {
            return OperationError.Invalid(
                "Номер строки основания расходного ордера должен быть положительным.");
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

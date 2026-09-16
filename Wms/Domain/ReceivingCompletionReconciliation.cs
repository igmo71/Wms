using Wms.Common;
using Wms.Domain.Enums;

namespace Wms.Domain;

public sealed record ReceivingCompletionPlanLine(
    int LineNumber,
    Guid StockKeepingUnitId,
    decimal Quantity);

public sealed record ReceivingCompletionReconciliation(
    DateTimeOffset CheckedAtUtc,
    OrderSynchronizationAssessment Assessment,
    IReadOnlyList<ReceivingCompletionPlanLine> OriginalPlan,
    IReadOnlyList<ReceivingCompletionPlanLine> CurrentPlan,
    IReadOnlyList<ReceivingCompletionPlanLine> Fact,
    bool HasQuantityDifference)
{
    public bool IsBlocking => Assessment.Level == OrderSynchronizationLevel.Blocking;
    public bool RequiresManager => !IsBlocking && HasQuantityDifference;
}

public static class ReceivingCompletionComparer
{
    public static ReceivingCompletionReconciliation Compare(
        ReceivingOrder order,
        ReceivingOrderImportSnapshot snapshot,
        DateTimeOffset checkedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(snapshot);

        string sourceFingerprint = ReceivingOrderSynchronizationComparer.CreateFingerprint(snapshot);
        var fingerprint = new OrderSynchronizationFingerprintBuilder();
        fingerprint.Add("source", sourceFingerprint);
        foreach (var lpn in order.LicensePlateNumbers.OrderBy(x => x.Id))
        {
            string prefix = $"lpn[{lpn.Id:N}]";
            fingerprint.Add($"{prefix}.status", lpn.Status);
            foreach (var item in lpn.Items.OrderBy(x => x.Id))
            {
                string itemPrefix = $"{prefix}.item[{item.Id:N}]";
                fingerprint.Add($"{itemPrefix}.sku", item.StockKeepingUnitId);
                fingerprint.Add($"{itemPrefix}.quantity", item.Quantity);
                fingerprint.Add($"{itemPrefix}.deleted", item.IsDeleted);
            }
        }

        var comparison = new OrderSynchronizationComparisonBuilder(fingerprint.Build());
        if (snapshot.Id != order.Id)
            comparison.Add("id", "Идентификатор ордера", order.Id, snapshot.Id, OrderSynchronizationLevel.Blocking);
        if (snapshot.DeletionMark)
            comparison.Add("deletionMark", "Пометка удаления", false, true, OrderSynchronizationLevel.Blocking);
        if (!snapshot.Posted)
            comparison.Add("posted", "Проведение", true, false, OrderSynchronizationLevel.Blocking);
        if (snapshot.Status == ReceivingOrderStatus.Unknown)
            comparison.Add("status", "Статус", "Поддерживаемый статус 1С", snapshot.Status, OrderSynchronizationLevel.Blocking);
        comparison.AddIfDifferent("warehouse", "Склад", order.WarehouseId, snapshot.WarehouseId, OrderSynchronizationLevel.Blocking);
        comparison.AddIfDifferent("warehouseOperation", "Складская операция", order.WarehouseOperation, snapshot.WarehouseOperation, OrderSynchronizationLevel.Blocking);
        comparison.AddIfDifferent("businessOperation", "Хозяйственная операция", order.BusinessOperation, snapshot.BusinessOperation, OrderSynchronizationLevel.Blocking);
        comparison.AddIfDifferent("baseOrder.id", "Документ-основание", order.BaseOrderId, snapshot.BaseOrderId, OrderSynchronizationLevel.Blocking);
        comparison.AddIfDifferent("baseOrder.type", "Тип документа-основания", order.BaseOrderType, snapshot.BaseOrderType, OrderSynchronizationLevel.Blocking);

        var originalPlan = order.Items
            .OrderBy(x => x.LineNumber)
            .Select(x => new ReceivingCompletionPlanLine(x.LineNumber, x.StockKeepingUnitId, x.PlanQuantity))
            .ToList();
        var fact = order.Items
            .OrderBy(x => x.LineNumber)
            .Select(x => new ReceivingCompletionPlanLine(x.LineNumber, x.StockKeepingUnitId, x.FactQuantity ?? 0))
            .ToList();
        var currentPlan = (snapshot.Items ?? [])
            .OrderBy(x => x.LineNumber)
            .Select(x => new ReceivingCompletionPlanLine(x.LineNumber, x.StockKeepingUnitId, x.Quantity))
            .ToList();

        if (snapshot.Items is null)
        {
            comparison.Add("items", "Строки", order.Items.Count, null, OrderSynchronizationLevel.Blocking);
            return Build(false);
        }

        bool invalidSemantics = snapshot.Items.Any(x =>
            x.LineNumber <= 0
            || x.StockKeepingUnitId == Guid.Empty
            || !WarehouseQuantity.IsNonNegative(x.Quantity)
            || !WarehouseQuantity.IsNonNegative(x.PlanQuantity)
            || x.Quantity != x.PlanQuantity);
        if (invalidSemantics)
        {
            comparison.Add("items.units", "Семантика количества", "Количество равно количеству упаковок", "Значения различаются", OrderSynchronizationLevel.Blocking);
        }

        bool hasDuplicateSku = snapshot.Items.GroupBy(x => x.StockKeepingUnitId).Any(x => x.Count() > 1);
        bool hasDuplicateLine = snapshot.Items.GroupBy(x => x.LineNumber).Any(x => x.Count() > 1);
        if (hasDuplicateSku)
            comparison.Add("items.duplicateSku", "Повтор SKU", "Одна строка на SKU", "Есть повторы", OrderSynchronizationLevel.Blocking);
        if (hasDuplicateLine)
            comparison.Add("items.duplicateLine", "Повтор номера строки", "Одна строка на номер", "Есть повторы", OrderSynchronizationLevel.Blocking);

        bool sameComposition = !hasDuplicateSku
            && !hasDuplicateLine
            && snapshot.Items.Count == order.Items.Count
            && order.Items.All(local => snapshot.Items.Any(source =>
                source.LineNumber == local.LineNumber
                && source.StockKeepingUnitId == local.StockKeepingUnitId));
        if (!sameComposition)
        {
            comparison.Add("items.composition", "Состав строк", "Исходный состав и связи SKU", "Состав или связи строк изменены", OrderSynchronizationLevel.Blocking);
            return Build(false);
        }

        bool hasQuantityDifference = false;
        foreach (var local in order.Items.OrderBy(x => x.LineNumber))
        {
            var source = snapshot.Items.Single(x => x.LineNumber == local.LineNumber);
            if (local.FactQuantity is null)
            {
                comparison.Add($"items[{local.LineNumber}].fact", $"Строка {local.LineNumber}: факт", "Не проверено", source.Quantity, OrderSynchronizationLevel.Blocking);
                continue;
            }

            if (local.FactQuantity != source.Quantity)
            {
                hasQuantityDifference = true;
                comparison.Add($"items[{local.LineNumber}].quantity", $"Строка {local.LineNumber}: количество", local.FactQuantity, source.Quantity, OrderSynchronizationLevel.RequiresOperatorDecision);
            }
        }

        return Build(hasQuantityDifference);

        ReceivingCompletionReconciliation Build(bool quantityDifference) => new(
            checkedAtUtc,
            comparison.Build(),
            originalPlan,
            currentPlan,
            fact,
            quantityDifference);
    }
}

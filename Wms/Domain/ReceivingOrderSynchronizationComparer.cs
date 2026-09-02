using Wms.Domain.Enums;

namespace Wms.Domain;

public static class ReceivingOrderSynchronizationComparer
{
    public static OrderSynchronizationAssessment Compare(
        ReceivingOrder order,
        ReceivingOrderImportSnapshot snapshot) =>
        Compare(order, snapshot, expectReceivedTarget: order.Status == ReceivingOrderStatus.Received);

    public static OrderSynchronizationAssessment CompareReceivedTarget(
        ReceivingOrder order,
        ReceivingOrderImportSnapshot snapshot) =>
        Compare(order, snapshot, expectReceivedTarget: true);

    private static OrderSynchronizationAssessment Compare(
        ReceivingOrder order,
        ReceivingOrderImportSnapshot snapshot,
        bool expectReceivedTarget)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(snapshot);

        var comparison = new OrderSynchronizationComparisonBuilder(CreateFingerprint(snapshot));
        ReceivingOrderStatus expectedStatus = expectReceivedTarget
            ? ReceivingOrderStatus.Received
            : order.Status;
        bool expectedPosted = expectReceivedTarget || order.Posted;

        comparison.AddIfDifferent("id", "Идентификатор ордера", order.Id, snapshot.Id, OrderSynchronizationLevel.Blocking);
        comparison.AddIfDifferent("deletionMark", "Пометка удаления", order.DeletionMark, snapshot.DeletionMark, OrderSynchronizationLevel.Blocking);
        comparison.AddIfDifferent("posted", "Проведение", expectedPosted, snapshot.Posted, OrderSynchronizationLevel.Blocking);
        comparison.AddIfDifferent("number", "Номер", order.Number, snapshot.Number, OrderSynchronizationLevel.RequiresOperatorDecision);
        comparison.AddIfDifferent("date", "Дата", order.Date, snapshot.Date, OrderSynchronizationLevel.RequiresOperatorDecision);
        comparison.AddIfDifferent("warehouse", "Склад", order.WarehouseId, snapshot.WarehouseId, OrderSynchronizationLevel.Blocking);
        comparison.AddIfDifferent("comment", "Комментарий", order.Comment, snapshot.Comment, OrderSynchronizationLevel.RequiresOperatorDecision);

        if (expectedStatus != snapshot.Status)
        {
            comparison.Add(
                "status",
                "Статус",
                expectedStatus,
                snapshot.Status,
                !expectReceivedTarget && IsCompatibleStatusChange(expectedStatus, snapshot.Status)
                    ? OrderSynchronizationLevel.RequiresOperatorDecision
                    : OrderSynchronizationLevel.Blocking);
        }

        comparison.AddIfDifferent("queue", "Очередь", order.Queue, snapshot.Queue, OrderSynchronizationLevel.RequiresOperatorDecision);
        comparison.AddIfDifferent("warehouseOperation", "Складская операция", order.WarehouseOperation, snapshot.WarehouseOperation, OrderSynchronizationLevel.Blocking);
        comparison.AddIfDifferent("businessOperation", "Хозяйственная операция", order.BusinessOperation, snapshot.BusinessOperation, OrderSynchronizationLevel.Blocking);
        comparison.AddIfDifferent("shipper.id", "Отправитель", order.ShipperId, snapshot.ShipperId, OrderSynchronizationLevel.RequiresOperatorDecision);
        comparison.AddIfDifferent("shipper.type", "Тип отправителя", order.ShipperType, snapshot.ShipperType, OrderSynchronizationLevel.RequiresOperatorDecision);
        comparison.AddIfDifferent("baseOrder.id", "Документ-основание", order.BaseOrderId, snapshot.BaseOrderId, OrderSynchronizationLevel.Blocking);
        comparison.AddIfDifferent("baseOrder.type", "Тип документа-основания", order.BaseOrderType, snapshot.BaseOrderType, OrderSynchronizationLevel.Blocking);

        CompareItems(comparison, order, snapshot.Items, expectReceivedTarget);
        return comparison.Build();
    }

    private static void CompareItems(
        OrderSynchronizationComparisonBuilder comparison,
        ReceivingOrder order,
        IReadOnlyCollection<ReceivingOrderItemImportSnapshot>? externalItems,
        bool expectReceivedTarget)
    {
        if (externalItems is null)
        {
            comparison.Add("items", "Строки", order.Items.Count, null, OrderSynchronizationLevel.Blocking);
            return;
        }

        var localByLine = order.Items.ToDictionary(x => x.LineNumber);
        var externalByLine = externalItems.ToLookup(x => x.LineNumber);
        var lineNumbers = localByLine.Keys
            .Concat(externalItems.Select(x => x.LineNumber))
            .Distinct()
            .Order()
            .ToList();

        foreach (int lineNumber in lineNumbers)
        {
            bool hasLocal = localByLine.TryGetValue(lineNumber, out ReceivingOrderItem? localItem);
            List<ReceivingOrderItemImportSnapshot> externalLines = externalByLine[lineNumber].ToList();
            string prefix = $"items[{lineNumber}]";

            if (!hasLocal || externalLines.Count != 1)
            {
                comparison.Add(
                    prefix,
                    $"Строка {lineNumber}: состав",
                    hasLocal ? "Есть" : "Отсутствует",
                    externalLines.Count == 0 ? "Отсутствует" : $"Количество строк: {externalLines.Count}",
                    OrderSynchronizationLevel.Blocking);
                continue;
            }

            ReceivingOrderItemImportSnapshot externalItem = externalLines[0];
            comparison.AddIfDifferent($"{prefix}.sku", $"Строка {lineNumber}: номенклатура", localItem!.StockKeepingUnitId, externalItem.StockKeepingUnitId, OrderSynchronizationLevel.Blocking);

            decimal? expectedQuantity = expectReceivedTarget
                ? localItem.FactQuantity
                : localItem.PlanQuantity;
            if (expectedQuantity is null)
            {
                comparison.Add(
                    $"{prefix}.factQuantity",
                    $"Строка {lineNumber}: фактическое количество",
                    "Не подтверждено",
                    externalItem.Quantity,
                    OrderSynchronizationLevel.Blocking);
                continue;
            }

            string quantityKind = expectReceivedTarget ? "фактическое" : "плановое";
            comparison.AddIfDifferent($"{prefix}.quantity", $"Строка {lineNumber}: {quantityKind} количество", expectedQuantity.Value, externalItem.Quantity, OrderSynchronizationLevel.Blocking);
            comparison.AddIfDifferent($"{prefix}.packageQuantity", $"Строка {lineNumber}: {quantityKind} количество упаковок", expectedQuantity.Value, externalItem.PlanQuantity, OrderSynchronizationLevel.Blocking);

            if (expectReceivedTarget && order.HasPlanFactDifference)
            {
                comparison.AddIfDifferent($"{prefix}.comment", $"Строка {lineNumber}: комментарий", localItem.Comment, externalItem.Comment, OrderSynchronizationLevel.Blocking);
            }
        }
    }

    private static bool IsCompatibleStatusChange(
        ReceivingOrderStatus localStatus,
        ReceivingOrderStatus externalStatus) =>
        IsActiveReceivingStatus(localStatus) && IsActiveReceivingStatus(externalStatus);

    private static bool IsActiveReceivingStatus(ReceivingOrderStatus status) =>
        status is ReceivingOrderStatus.InReceiving or ReceivingOrderStatus.ProcessingRequired;

    private static string CreateFingerprint(ReceivingOrderImportSnapshot snapshot)
    {
        var fingerprint = new OrderSynchronizationFingerprintBuilder();
        fingerprint.Add("id", snapshot.Id);
        fingerprint.Add("deletionMark", snapshot.DeletionMark);
        fingerprint.Add("posted", snapshot.Posted);
        fingerprint.Add("number", snapshot.Number);
        fingerprint.Add("date", snapshot.Date);
        fingerprint.Add("warehouse", snapshot.WarehouseId);
        fingerprint.Add("comment", snapshot.Comment);
        fingerprint.Add("status", snapshot.Status);
        fingerprint.Add("queue", snapshot.Queue);
        fingerprint.Add("warehouseOperation", snapshot.WarehouseOperation);
        fingerprint.Add("businessOperation", snapshot.BusinessOperation);
        fingerprint.Add("shipper.id", snapshot.ShipperId);
        fingerprint.Add("shipper.type", snapshot.ShipperType);
        fingerprint.Add("baseOrder.id", snapshot.BaseOrderId);
        fingerprint.Add("baseOrder.type", snapshot.BaseOrderType);

        if (snapshot.Items is null)
        {
            fingerprint.Add("items", null);
        }
        else
        {
            fingerprint.Add("items.count", snapshot.Items.Count);
            int index = 0;
            foreach (ReceivingOrderItemImportSnapshot item in snapshot.Items
                .OrderBy(x => x.LineNumber)
                .ThenBy(x => x.StockKeepingUnitId)
                .ThenBy(x => x.PlanQuantity)
                .ThenBy(x => x.Quantity)
                .ThenBy(x => x.Comment))
            {
                string prefix = $"items[{index++}]";
                fingerprint.Add($"{prefix}.line", item.LineNumber);
                fingerprint.Add($"{prefix}.sku", item.StockKeepingUnitId);
                fingerprint.Add($"{prefix}.packageQuantity", item.PlanQuantity);
                fingerprint.Add($"{prefix}.quantity", item.Quantity);
                fingerprint.Add($"{prefix}.comment", item.Comment);
            }
        }

        return fingerprint.Build();
    }
}

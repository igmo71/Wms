using Wms.Domain.Enums;

namespace Wms.Domain;

public static class ShippingOrderSynchronizationComparer
{
    public static OrderSynchronizationAssessment Compare(
        ShippingOrder order,
        ShippingOrderImportSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(snapshot);

        var comparison = new OrderSynchronizationComparisonBuilder(CreateFingerprint(snapshot));

        comparison.AddIfDifferent("id", "Идентификатор ордера", order.Id, snapshot.Id, OrderSynchronizationLevel.Blocking);
        comparison.AddIfDifferent("deletionMark", "Пометка удаления", order.DeletionMark, snapshot.DeletionMark, OrderSynchronizationLevel.Blocking);
        comparison.AddIfDifferent("posted", "Проведение", order.Posted, snapshot.Posted, OrderSynchronizationLevel.Blocking);
        comparison.AddIfDifferent("number", "Номер", order.Number, snapshot.Number, OrderSynchronizationLevel.RequiresOperatorDecision);
        comparison.AddIfDifferent("date", "Дата", order.Date, snapshot.Date, OrderSynchronizationLevel.RequiresOperatorDecision);
        comparison.AddIfDifferent("warehouse", "Склад", order.WarehouseId, snapshot.WarehouseId, OrderSynchronizationLevel.Blocking);
        comparison.AddIfDifferent("comment", "Комментарий", order.Comment, snapshot.Comment, OrderSynchronizationLevel.RequiresOperatorDecision);

        if (order.Status != snapshot.Status)
        {
            comparison.Add(
                "status",
                "Статус",
                order.Status,
                snapshot.Status,
                IsCompatibleStatusChange(order.Status, snapshot.Status)
                    ? OrderSynchronizationLevel.RequiresOperatorDecision
                    : OrderSynchronizationLevel.Blocking);
        }

        comparison.AddIfDifferent("queue", "Очередь", order.Queue, snapshot.Queue, OrderSynchronizationLevel.RequiresOperatorDecision);
        comparison.AddIfDifferent("plannedShippingDate", "Планируемая дата отгрузки", order.PlannedShippingDate, snapshot.PlannedShippingDate, OrderSynchronizationLevel.RequiresOperatorDecision);
        comparison.AddIfDifferent("deliveryDirection", "Направление доставки", order.DeliveryDirectionId, snapshot.DeliveryDirectionId, OrderSynchronizationLevel.RequiresOperatorDecision);
        comparison.AddIfDifferent("warehouseOperation", "Складская операция", order.WarehouseOperation, snapshot.WarehouseOperation, OrderSynchronizationLevel.Blocking);
        comparison.AddIfDifferent("receiver.id", "Получатель", order.ReceiverId, snapshot.ReceiverId, OrderSynchronizationLevel.RequiresOperatorDecision);
        comparison.AddIfDifferent("receiver.type", "Тип получателя", order.ReceiverType, snapshot.ReceiverType, OrderSynchronizationLevel.RequiresOperatorDecision);

        CompareItems(comparison, order.Items, snapshot.Items);
        CompareBaseItems(comparison, order.BaseItems, snapshot.BaseItems);
        return comparison.Build();
    }

    private static void CompareItems(
        OrderSynchronizationComparisonBuilder comparison,
        IReadOnlyCollection<ShippingOrderItem> localItems,
        IReadOnlyCollection<ShippingOrderItemImportSnapshot>? externalItems)
    {
        if (externalItems is null)
        {
            comparison.Add("items", "Отгружаемые строки", localItems.Count, null, OrderSynchronizationLevel.Blocking);
            return;
        }

        var localByLine = localItems.ToDictionary(x => x.LineNumber);
        var externalByLine = externalItems.ToLookup(x => x.LineNumber);
        var lineNumbers = localByLine.Keys
            .Concat(externalItems.Select(x => x.LineNumber))
            .Distinct()
            .Order()
            .ToList();

        foreach (int lineNumber in lineNumbers)
        {
            bool hasLocal = localByLine.TryGetValue(lineNumber, out ShippingOrderItem? localItem);
            List<ShippingOrderItemImportSnapshot> externalLines = externalByLine[lineNumber].ToList();
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

            ShippingOrderItemImportSnapshot externalItem = externalLines[0];
            comparison.AddIfDifferent($"{prefix}.sku", $"Строка {lineNumber}: номенклатура", localItem!.StockKeepingUnitId, externalItem.StockKeepingUnitId, OrderSynchronizationLevel.Blocking);
            comparison.AddIfDifferent($"{prefix}.planQuantity", $"Строка {lineNumber}: плановое количество", localItem.PlanQuantity, externalItem.PlanQuantity, OrderSynchronizationLevel.Blocking);
        }
    }

    private static void CompareBaseItems(
        OrderSynchronizationComparisonBuilder comparison,
        IReadOnlyCollection<ShippingOrderBaseItem> localItems,
        IReadOnlyCollection<ShippingOrderBaseItemImportSnapshot>? externalItems)
    {
        if (externalItems is null)
        {
            comparison.Add("baseItems", "Строки основания", localItems.Count, null, OrderSynchronizationLevel.Blocking);
            return;
        }

        var localByLine = localItems.ToDictionary(x => x.LineNumber);
        var externalByLine = externalItems.ToLookup(x => x.LineNumber);
        var lineNumbers = localByLine.Keys
            .Concat(externalItems.Select(x => x.LineNumber))
            .Distinct()
            .Order()
            .ToList();

        foreach (int lineNumber in lineNumbers)
        {
            bool hasLocal = localByLine.TryGetValue(lineNumber, out ShippingOrderBaseItem? localItem);
            List<ShippingOrderBaseItemImportSnapshot> externalLines = externalByLine[lineNumber].ToList();
            string prefix = $"baseItems[{lineNumber}]";

            if (!hasLocal || externalLines.Count != 1)
            {
                comparison.Add(
                    prefix,
                    $"Строка основания {lineNumber}: состав",
                    hasLocal ? "Есть" : "Отсутствует",
                    externalLines.Count == 0 ? "Отсутствует" : $"Количество строк: {externalLines.Count}",
                    OrderSynchronizationLevel.Blocking);
                continue;
            }

            ShippingOrderBaseItemImportSnapshot externalItem = externalLines[0];
            comparison.AddIfDifferent($"{prefix}.sku", $"Строка основания {lineNumber}: номенклатура", localItem!.StockKeepingUnitId, externalItem.StockKeepingUnitId, OrderSynchronizationLevel.Blocking);
            comparison.AddIfDifferent($"{prefix}.planQuantity", $"Строка основания {lineNumber}: количество", localItem.PlanQuantity, externalItem.PlanQuantity, OrderSynchronizationLevel.Blocking);
            comparison.AddIfDifferent($"{prefix}.baseOrderId", $"Строка основания {lineNumber}: документ", localItem.BaseOrderId, externalItem.BaseOrderId, OrderSynchronizationLevel.Blocking);
            comparison.AddIfDifferent($"{prefix}.baseOrderType", $"Строка основания {lineNumber}: тип документа", localItem.BaseOrderType, externalItem.BaseOrderType, OrderSynchronizationLevel.Blocking);
        }
    }

    private static bool IsCompatibleStatusChange(
        ShippingOrderStatus localStatus,
        ShippingOrderStatus externalStatus) =>
        IsPickingStatus(localStatus) && IsPickingStatus(externalStatus);

    private static bool IsPickingStatus(ShippingOrderStatus status) =>
        status is ShippingOrderStatus.ReadyForPicking
            or ShippingOrderStatus.ReadyForVerification
            or ShippingOrderStatus.InVerification
            or ShippingOrderStatus.Verified;

    private static string CreateFingerprint(ShippingOrderImportSnapshot snapshot)
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
        fingerprint.Add("plannedShippingDate", snapshot.PlannedShippingDate);
        fingerprint.Add("deliveryDirection", snapshot.DeliveryDirectionId);
        fingerprint.Add("warehouseOperation", snapshot.WarehouseOperation);
        fingerprint.Add("receiver.id", snapshot.ReceiverId);
        fingerprint.Add("receiver.type", snapshot.ReceiverType);
        AddItems(fingerprint, snapshot.Items);
        AddBaseItems(fingerprint, snapshot.BaseItems);
        return fingerprint.Build();
    }

    private static void AddItems(
        OrderSynchronizationFingerprintBuilder fingerprint,
        IReadOnlyCollection<ShippingOrderItemImportSnapshot>? items)
    {
        if (items is null)
        {
            fingerprint.Add("items", null);
            return;
        }

        fingerprint.Add("items.count", items.Count);
        int index = 0;
        foreach (ShippingOrderItemImportSnapshot item in items
            .OrderBy(x => x.LineNumber)
            .ThenBy(x => x.StockKeepingUnitId)
            .ThenBy(x => x.PlanQuantity))
        {
            string prefix = $"items[{index++}]";
            fingerprint.Add($"{prefix}.line", item.LineNumber);
            fingerprint.Add($"{prefix}.sku", item.StockKeepingUnitId);
            fingerprint.Add($"{prefix}.planQuantity", item.PlanQuantity);
        }
    }

    private static void AddBaseItems(
        OrderSynchronizationFingerprintBuilder fingerprint,
        IReadOnlyCollection<ShippingOrderBaseItemImportSnapshot>? items)
    {
        if (items is null)
        {
            fingerprint.Add("baseItems", null);
            return;
        }

        fingerprint.Add("baseItems.count", items.Count);
        int index = 0;
        foreach (ShippingOrderBaseItemImportSnapshot item in items
            .OrderBy(x => x.LineNumber)
            .ThenBy(x => x.StockKeepingUnitId)
            .ThenBy(x => x.PlanQuantity)
            .ThenBy(x => x.BaseOrderId)
            .ThenBy(x => x.BaseOrderType))
        {
            string prefix = $"baseItems[{index++}]";
            fingerprint.Add($"{prefix}.line", item.LineNumber);
            fingerprint.Add($"{prefix}.sku", item.StockKeepingUnitId);
            fingerprint.Add($"{prefix}.planQuantity", item.PlanQuantity);
            fingerprint.Add($"{prefix}.baseOrderId", item.BaseOrderId);
            fingerprint.Add($"{prefix}.baseOrderType", item.BaseOrderType);
        }
    }
}

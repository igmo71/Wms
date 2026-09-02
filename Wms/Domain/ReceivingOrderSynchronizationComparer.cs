using Wms.Domain.Enums;

namespace Wms.Domain;

public static class ReceivingOrderSynchronizationComparer
{
    public static OrderSynchronizationAssessment Compare(
        ReceivingOrder order,
        ReceivingOrderImportSnapshot snapshot)
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
        comparison.AddIfDifferent("warehouseOperation", "Складская операция", order.WarehouseOperation, snapshot.WarehouseOperation, OrderSynchronizationLevel.Blocking);
        comparison.AddIfDifferent("businessOperation", "Хозяйственная операция", order.BusinessOperation, snapshot.BusinessOperation, OrderSynchronizationLevel.Blocking);
        comparison.AddIfDifferent("shipper.id", "Отправитель", order.ShipperId, snapshot.ShipperId, OrderSynchronizationLevel.RequiresOperatorDecision);
        comparison.AddIfDifferent("shipper.type", "Тип отправителя", order.ShipperType, snapshot.ShipperType, OrderSynchronizationLevel.RequiresOperatorDecision);
        comparison.AddIfDifferent("baseOrder.id", "Документ-основание", order.BaseOrderId, snapshot.BaseOrderId, OrderSynchronizationLevel.Blocking);
        comparison.AddIfDifferent("baseOrder.type", "Тип документа-основания", order.BaseOrderType, snapshot.BaseOrderType, OrderSynchronizationLevel.Blocking);

        CompareItems(comparison, order.Items, snapshot.Items);
        return comparison.Build();
    }

    private static void CompareItems(
        OrderSynchronizationComparisonBuilder comparison,
        IReadOnlyCollection<ReceivingOrderItem> localItems,
        IReadOnlyCollection<ReceivingOrderItemImportSnapshot>? externalItems)
    {
        if (externalItems is null)
        {
            comparison.Add("items", "Строки", localItems.Count, null, OrderSynchronizationLevel.Blocking);
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
            comparison.AddIfDifferent($"{prefix}.planQuantity", $"Строка {lineNumber}: плановое количество", localItem.PlanQuantity, externalItem.PlanQuantity, OrderSynchronizationLevel.Blocking);
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
                .ThenBy(x => x.PlanQuantity))
            {
                string prefix = $"items[{index++}]";
                fingerprint.Add($"{prefix}.line", item.LineNumber);
                fingerprint.Add($"{prefix}.sku", item.StockKeepingUnitId);
                fingerprint.Add($"{prefix}.planQuantity", item.PlanQuantity);
            }
        }

        return fingerprint.Build();
    }
}

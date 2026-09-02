using Wms.Domain.Enums;

namespace Wms.Domain;

public static class ShippingOrderSynchronizationComparer
{
    private enum ExpectedTarget
    {
        Source,
        ReadyForShipment,
        Shipped
    }

    public static OrderSynchronizationAssessment Compare(
        ShippingOrder order,
        ShippingOrderImportSnapshot snapshot) =>
        Compare(order, snapshot, order.Status switch
        {
            ShippingOrderStatus.ReadyForShipment => ExpectedTarget.ReadyForShipment,
            ShippingOrderStatus.Shipped => ExpectedTarget.Shipped,
            _ => ExpectedTarget.Source
        });

    public static OrderSynchronizationAssessment CompareReadyForShipmentTarget(
        ShippingOrder order,
        ShippingOrderImportSnapshot snapshot) =>
        Compare(order, snapshot, ExpectedTarget.ReadyForShipment);

    public static OrderSynchronizationAssessment CompareShippedTarget(
        ShippingOrder order,
        ShippingOrderImportSnapshot snapshot) =>
        Compare(order, snapshot, ExpectedTarget.Shipped);

    private static OrderSynchronizationAssessment Compare(
        ShippingOrder order,
        ShippingOrderImportSnapshot snapshot,
        ExpectedTarget target)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(snapshot);

        var comparison = new OrderSynchronizationComparisonBuilder(CreateFingerprint(snapshot));
        ShippingOrderStatus expectedStatus = target switch
        {
            ExpectedTarget.ReadyForShipment => ShippingOrderStatus.ReadyForShipment,
            ExpectedTarget.Shipped => ShippingOrderStatus.Shipped,
            _ => order.Status
        };
        bool expectedPosted = target != ExpectedTarget.Source || order.Posted;

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
                target == ExpectedTarget.Source && IsCompatibleStatusChange(expectedStatus, snapshot.Status)
                    ? OrderSynchronizationLevel.RequiresOperatorDecision
                    : OrderSynchronizationLevel.Blocking);
        }

        comparison.AddIfDifferent("queue", "Очередь", order.Queue, snapshot.Queue, OrderSynchronizationLevel.RequiresOperatorDecision);
        comparison.AddIfDifferent("plannedShippingDate", "Планируемая дата отгрузки", order.PlannedShippingDate, snapshot.PlannedShippingDate, OrderSynchronizationLevel.RequiresOperatorDecision);
        comparison.AddIfDifferent("deliveryDirection", "Направление доставки", order.DeliveryDirectionId, snapshot.DeliveryDirectionId, OrderSynchronizationLevel.RequiresOperatorDecision);
        comparison.AddIfDifferent("warehouseOperation", "Складская операция", order.WarehouseOperation, snapshot.WarehouseOperation, OrderSynchronizationLevel.Blocking);
        comparison.AddIfDifferent("receiver.id", "Получатель", order.ReceiverId, snapshot.ReceiverId, OrderSynchronizationLevel.RequiresOperatorDecision);
        comparison.AddIfDifferent("receiver.type", "Тип получателя", order.ReceiverType, snapshot.ReceiverType, OrderSynchronizationLevel.RequiresOperatorDecision);

        if (target == ExpectedTarget.Source)
        {
            CompareSourceItems(comparison, order.Items, snapshot.Items);
            CompareSourceBaseItems(comparison, order.BaseItems, snapshot.BaseItems);
        }
        else
        {
            CompareTargetItems(comparison, order.Items, snapshot.Items);
            CompareTargetBaseItems(comparison, order.Items, order.BaseItems, snapshot.BaseItems);
        }

        return comparison.Build();
    }

    private static void CompareSourceItems(
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
                comparison.Add(prefix, $"Строка {lineNumber}: состав", hasLocal ? "Есть" : "Отсутствует", DescribeLineCount(externalLines.Count), OrderSynchronizationLevel.Blocking);
                continue;
            }

            ShippingOrderItemImportSnapshot externalItem = externalLines[0];
            comparison.AddIfDifferent($"{prefix}.sku", $"Строка {lineNumber}: номенклатура", localItem!.StockKeepingUnitId, externalItem.StockKeepingUnitId, OrderSynchronizationLevel.Blocking);
            comparison.AddIfDifferent($"{prefix}.quantity", $"Строка {lineNumber}: количество", localItem.PlanQuantity, externalItem.Quantity, OrderSynchronizationLevel.Blocking);
            comparison.AddIfDifferent($"{prefix}.packageQuantity", $"Строка {lineNumber}: количество упаковок", localItem.PlanQuantity, externalItem.PlanQuantity, OrderSynchronizationLevel.Blocking);
            comparison.AddIfDifferent($"{prefix}.action", $"Строка {lineNumber}: действие", ShippingOrderAction.PickUp, externalItem.Action, OrderSynchronizationLevel.Blocking);
        }
    }

    private static void CompareTargetItems(
        OrderSynchronizationComparisonBuilder comparison,
        IReadOnlyCollection<ShippingOrderItem> localItems,
        IReadOnlyCollection<ShippingOrderItemImportSnapshot>? externalItems)
    {
        if (externalItems is null)
        {
            comparison.Add("items", "Отгружаемые строки", "Ожидаемый результат WMS", null, OrderSynchronizationLevel.Blocking);
            return;
        }

        if (localItems.GroupBy(x => x.StockKeepingUnitId).Any(x => x.Count() > 1))
        {
            comparison.Add("items.mapping", "Сопоставление отгружаемых строк", "Однозначные строки", "В WMS повторяется номенклатура", OrderSynchronizationLevel.Blocking);
            return;
        }

        var localBySku = localItems.ToDictionary(x => x.StockKeepingUnitId);
        var externalBySku = externalItems.ToLookup(x => x.StockKeepingUnitId);
        int expectedCount = localItems.Count
            + localItems.Count(x => x.FactQuantity > 0 && x.FactQuantity < x.PlanQuantity);
        comparison.AddIfDifferent("items.count", "Количество отгружаемых строк", expectedCount, externalItems.Count, OrderSynchronizationLevel.Blocking);

        foreach (Guid externalSku in externalItems.Select(x => x.StockKeepingUnitId).Distinct())
        {
            if (!localBySku.ContainsKey(externalSku))
            {
                comparison.Add($"items.sku[{externalSku:N}]", "Неожиданная номенклатура в отгружаемых строках", null, externalSku, OrderSynchronizationLevel.Blocking);
            }
        }

        foreach (ShippingOrderItem localItem in localItems.OrderBy(x => x.LineNumber))
        {
            List<ShippingOrderItemImportSnapshot> matchingItems = externalBySku[localItem.StockKeepingUnitId].ToList();
            int expectedMatchingCount = localItem.FactQuantity > 0 && localItem.FactQuantity < localItem.PlanQuantity ? 2 : 1;
            string prefix = $"items[{localItem.LineNumber}]";
            if (matchingItems.Count != expectedMatchingCount)
            {
                comparison.Add(prefix, $"Строка {localItem.LineNumber}: состав результата", $"Количество строк: {expectedMatchingCount}", DescribeLineCount(matchingItems.Count), OrderSynchronizationLevel.Blocking);
                continue;
            }

            List<ShippingOrderItemImportSnapshot> primaryItems = matchingItems
                .Where(x => x.LineNumber == localItem.LineNumber)
                .ToList();
            if (primaryItems.Count != 1)
            {
                comparison.Add($"{prefix}.primary", $"Строка {localItem.LineNumber}: основная строка", "Одна строка с исходным номером", DescribeLineCount(primaryItems.Count), OrderSynchronizationLevel.Blocking);
                continue;
            }

            ShippingOrderItemImportSnapshot primaryItem = primaryItems[0];
            decimal primaryQuantity = localItem.FactQuantity > 0 ? localItem.FactQuantity : localItem.PlanQuantity;
            ShippingOrderAction primaryAction = localItem.FactQuantity > 0 ? ShippingOrderAction.Ship : ShippingOrderAction.DoNotShip;
            CompareTargetItemValues(comparison, prefix, localItem.LineNumber, primaryQuantity, primaryAction, primaryItem);

            if (expectedMatchingCount == 2)
            {
                ShippingOrderItemImportSnapshot residualItem = matchingItems.Single(x => x.LineNumber != localItem.LineNumber);
                decimal residualQuantity = localItem.PlanQuantity - localItem.FactQuantity;
                CompareTargetItemValues(comparison, $"{prefix}.residual", localItem.LineNumber, residualQuantity, ShippingOrderAction.DoNotShip, residualItem);
            }
        }
    }

    private static void CompareTargetItemValues(
        OrderSynchronizationComparisonBuilder comparison,
        string prefix,
        int sourceLineNumber,
        decimal expectedQuantity,
        ShippingOrderAction expectedAction,
        ShippingOrderItemImportSnapshot externalItem)
    {
        comparison.AddIfDifferent($"{prefix}.quantity", $"Строка {sourceLineNumber}: количество результата", expectedQuantity, externalItem.Quantity, OrderSynchronizationLevel.Blocking);
        comparison.AddIfDifferent($"{prefix}.packageQuantity", $"Строка {sourceLineNumber}: количество упаковок результата", expectedQuantity, externalItem.PlanQuantity, OrderSynchronizationLevel.Blocking);
        comparison.AddIfDifferent($"{prefix}.action", $"Строка {sourceLineNumber}: действие результата", expectedAction, externalItem.Action, OrderSynchronizationLevel.Blocking);
    }

    private static void CompareSourceBaseItems(
        OrderSynchronizationComparisonBuilder comparison,
        IReadOnlyCollection<ShippingOrderBaseItem> localItems,
        IReadOnlyCollection<ShippingOrderBaseItemImportSnapshot>? externalItems) =>
        CompareBaseItems(comparison, localItems, externalItems, null);

    private static void CompareTargetBaseItems(
        OrderSynchronizationComparisonBuilder comparison,
        IReadOnlyCollection<ShippingOrderItem> localItems,
        IReadOnlyCollection<ShippingOrderBaseItem> localBaseItems,
        IReadOnlyCollection<ShippingOrderBaseItemImportSnapshot>? externalItems)
    {
        if (localItems.GroupBy(x => x.StockKeepingUnitId).Any(x => x.Count() > 1)
            || localBaseItems.GroupBy(x => x.StockKeepingUnitId).Any(x => x.Count() > 1))
        {
            comparison.Add("baseItems.mapping", "Сопоставление строк основания", "Однозначные строки", "В WMS повторяется номенклатура", OrderSynchronizationLevel.Blocking);
            return;
        }

        var factsBySku = localItems.ToDictionary(x => x.StockKeepingUnitId, x => x.FactQuantity);
        List<ShippingOrderBaseItem> expectedItems = localBaseItems
            .Where(x => factsBySku.TryGetValue(x.StockKeepingUnitId, out decimal factQuantity) && factQuantity > 0)
            .ToList();

        if (localBaseItems.Any(x => !factsBySku.ContainsKey(x.StockKeepingUnitId))
            || localItems.Any(x => localBaseItems.All(baseItem => baseItem.StockKeepingUnitId != x.StockKeepingUnitId)))
        {
            comparison.Add("baseItems.mapping", "Сопоставление строк основания", "Одинаковая номенклатура", "Строки WMS не сопоставляются", OrderSynchronizationLevel.Blocking);
        }

        CompareBaseItems(comparison, expectedItems, externalItems, factsBySku);
    }

    private static void CompareBaseItems(
        OrderSynchronizationComparisonBuilder comparison,
        IReadOnlyCollection<ShippingOrderBaseItem> expectedItems,
        IReadOnlyCollection<ShippingOrderBaseItemImportSnapshot>? externalItems,
        IReadOnlyDictionary<Guid, decimal>? targetQuantities)
    {
        if (externalItems is null)
        {
            comparison.Add("baseItems", "Строки основания", expectedItems.Count, null, OrderSynchronizationLevel.Blocking);
            return;
        }

        var expectedByLine = expectedItems.ToDictionary(x => x.LineNumber);
        var externalByLine = externalItems.ToLookup(x => x.LineNumber);
        var lineNumbers = expectedByLine.Keys
            .Concat(externalItems.Select(x => x.LineNumber))
            .Distinct()
            .Order()
            .ToList();

        foreach (int lineNumber in lineNumbers)
        {
            bool hasExpected = expectedByLine.TryGetValue(lineNumber, out ShippingOrderBaseItem? expectedItem);
            List<ShippingOrderBaseItemImportSnapshot> externalLines = externalByLine[lineNumber].ToList();
            string prefix = $"baseItems[{lineNumber}]";
            if (!hasExpected || externalLines.Count != 1)
            {
                comparison.Add(prefix, $"Строка основания {lineNumber}: состав", hasExpected ? "Есть" : "Отсутствует", DescribeLineCount(externalLines.Count), OrderSynchronizationLevel.Blocking);
                continue;
            }

            ShippingOrderBaseItemImportSnapshot externalItem = externalLines[0];
            decimal expectedQuantity = targetQuantities is null
                ? expectedItem!.PlanQuantity
                : targetQuantities[expectedItem!.StockKeepingUnitId];
            comparison.AddIfDifferent($"{prefix}.sku", $"Строка основания {lineNumber}: номенклатура", expectedItem.StockKeepingUnitId, externalItem.StockKeepingUnitId, OrderSynchronizationLevel.Blocking);
            comparison.AddIfDifferent($"{prefix}.planQuantity", $"Строка основания {lineNumber}: количество", expectedQuantity, externalItem.PlanQuantity, OrderSynchronizationLevel.Blocking);
            comparison.AddIfDifferent($"{prefix}.baseOrderId", $"Строка основания {lineNumber}: документ", expectedItem.BaseOrderId, externalItem.BaseOrderId, OrderSynchronizationLevel.Blocking);
            comparison.AddIfDifferent($"{prefix}.baseOrderType", $"Строка основания {lineNumber}: тип документа", expectedItem.BaseOrderType, externalItem.BaseOrderType, OrderSynchronizationLevel.Blocking);
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

    private static string DescribeLineCount(int count) =>
        count == 0 ? "Отсутствует" : $"Количество строк: {count}";

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
            .ThenBy(x => x.PlanQuantity)
            .ThenBy(x => x.Quantity)
            .ThenBy(x => x.Action))
        {
            string prefix = $"items[{index++}]";
            fingerprint.Add($"{prefix}.line", item.LineNumber);
            fingerprint.Add($"{prefix}.sku", item.StockKeepingUnitId);
            fingerprint.Add($"{prefix}.packageQuantity", item.PlanQuantity);
            fingerprint.Add($"{prefix}.quantity", item.Quantity);
            fingerprint.Add($"{prefix}.action", item.Action);
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

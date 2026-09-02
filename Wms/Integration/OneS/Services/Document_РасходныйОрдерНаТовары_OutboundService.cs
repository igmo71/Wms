using Microsoft.Extensions.Logging;
using Wms.Application.ShippingOrders;
using Wms.Common;
using Wms.Domain;
using Wms.Domain.Enums;
using Wms.Integration.OneS.Models;
using Document = Wms.Integration.OneS.Models.Document_РасходныйОрдерНаТовары;

namespace Wms.Integration.OneS.Services;

public class Document_РасходныйОрдерНаТовары_OutboundService(
    OneCClient oneCClient,
    ILogger<Document_РасходныйОрдерНаТовары_OutboundService> logger)
{
    private record StatusOrderCommand(string Статус);

    internal Task<OperationResult> SetReadyForPickingAsync(Guid orderId, CancellationToken ct) =>
        SwitchStatusAsync("КОтбору", orderId, ct);

    internal Task<OperationResult> SetReadyForShipmentAsync(Guid orderId, CancellationToken ct) =>
        SwitchStatusAsync("КОтгрузке", orderId, ct);

    internal Task<OperationResult> SetShippedAsync(Guid orderId, CancellationToken ct) =>
        SwitchStatusAsync("Отгружен", orderId, ct);

    private async Task<OperationResult> SwitchStatusAsync(string expectedStatus, Guid orderId, CancellationToken ct)
    {
        using var scope = logger.BeginScope("SwitchStatus {OrderId} {ExpectedStatus}", orderId, expectedStatus);
        using var activity = AppTracing.StartActivity("Document_РасходныйОрдерНаТовары.SwitchStatus", nameof(ShippingOrderCommandService));

        var patchUri = Document.PatchUri(orderId.ToString());

        var patchResult = await oneCClient.PatchValueAsync<StatusOrderCommand, Document>(
            patchUri,
            new StatusOrderCommand(expectedStatus),
            ct);

        if (!patchResult.IsSuccess)
            return patchResult;

        var actualStatus = patchResult.Value?.Статус;

        if (actualStatus != expectedStatus)
        {
            logger.LogError("1С вернула неожиданный статус. Ожидался: {ExpectedStatus}, получен: {ActualStatus}", expectedStatus, actualStatus);
            return OperationError.Conflict($"1С вернула неожиданный статус. Ожидался «{expectedStatus}», получен «{actualStatus ?? "не указан"}».");
        }

        var postUri = Document.PostDocumentUri(orderId.ToString());
        return await oneCClient.PostValueAsync(postUri, ct);
    }

    internal async Task<OperationResult> UpdateDocumentItemsAsync(ShippingOrder shippingOrder, CancellationToken ct)
    {
        using var scope = logger.BeginScope("UpdateDocumentItems {OrderId}", shippingOrder.Id);
        using var activity = AppTracing.StartActivity("Document_РасходныйОрдерНаТовары.UpdateDocumentItems", nameof(ShippingOrderCommandService));

        var freshDocumentResult = await oneCClient.GetValueAsync<RootObject<Document>>(
            Document.GetUri(shippingOrder.Id.ToString()), ct);

        if (!freshDocumentResult.IsSuccess)
            return freshDocumentResult;

        var freshDocument = freshDocumentResult.Value?.Value?.SingleOrDefault();
        if (freshDocument is null)
            return OperationError.NotFound("Расходный ордер не найден в 1С.");

        if (HasAmbiguousSkuLines(shippingOrder))
            return OperationError.Conflict("Расходный ордер содержит несколько строк одной номенклатуры и не может быть безопасно обновлён.");

        // A mobile retry can arrive after 1C accepted this target table state but
        // before WMS committed its local transition and command receipt.
        if (IsTargetItemsState(shippingOrder, freshDocument))
            return OperationResult.Success();

        var patchBodyResult = CreatePatchBody(shippingOrder, freshDocument);
        if (!patchBodyResult.IsSuccess)
            return patchBodyResult;

        var patchUri = Document.PatchUri(shippingOrder.Id.ToString());
        var patchResult = await oneCClient.PatchValueAsync<PatchBody, object>(patchUri, patchBodyResult.Value!, ct);

        return patchResult.IsSuccess ? OperationResult.Success() : patchResult;
    }

    private static OperationResult<PatchBody> CreatePatchBody(ShippingOrder shippingOrder, Document freshDocument)
    {
        var freshBaseItemsByLine = freshDocument.ТоварыПоРаспоряжениям.ToDictionary(x => x.LineNumber);
        var freshItemsByLine = freshDocument.ОтгружаемыеТовары.ToDictionary(x => x.LineNumber);

        if (freshBaseItemsByLine.Count != shippingOrder.BaseItems.Count
            || freshItemsByLine.Count(x => Document.IsRegularShippingItem(x.Value)) != shippingOrder.Items.Count)
        {
            return OperationError.Conflict("План расходного ордера в 1С отличается от плана в WMS.");
        }

        foreach (var localBaseItem in shippingOrder.BaseItems)
        {
            if (!freshBaseItemsByLine.TryGetValue(localBaseItem.LineNumber, out var freshBaseItem)
                || freshBaseItem.Номенклатура_Key != localBaseItem.StockKeepingUnitId
                || freshBaseItem.Количество != localBaseItem.PlanQuantity)
            {
                return OperationError.Conflict("План расходного ордера в 1С отличается от плана в WMS.");
            }
        }

        foreach (var localItem in shippingOrder.Items)
        {
            if (!freshItemsByLine.TryGetValue(localItem.LineNumber, out var freshItem)
                || freshItem.Номенклатура_Key != localItem.StockKeepingUnitId
                || freshItem.КоличествоУпаковок != localItem.PlanQuantity
                || ODataEnumMapper.Parse<ShippingOrderAction>(freshItem.Действие) != ShippingOrderAction.PickUp)
            {
                return OperationError.Conflict("План расходного ордера в 1С отличается от плана в WMS.");
            }
        }

        var baseItemStockKeepingUnitIds = shippingOrder.BaseItems
            .Select(x => x.StockKeepingUnitId)
            .Order()
            .ToList();
        var itemStockKeepingUnitIds = shippingOrder.Items
            .Select(x => x.StockKeepingUnitId)
            .Order()
            .ToList();

        if (!baseItemStockKeepingUnitIds.SequenceEqual(itemStockKeepingUnitIds))
        {
            return OperationError.Conflict("Строки основания и строки расходного ордера в WMS нельзя однозначно сопоставить.");
        }

        var localItemsByStockKeepingUnit = shippingOrder.Items.ToDictionary(x => x.StockKeepingUnitId);
        var patchBaseItems = freshDocument.ТоварыПоРаспоряжениям
            .Where(x => localItemsByStockKeepingUnit.TryGetValue(x.Номенклатура_Key, out var localItem)
                && localItem.FactQuantity > 0)
            .Select(x =>
            {
                var result = Copy(x);
                result.Количество = localItemsByStockKeepingUnit[x.Номенклатура_Key].FactQuantity;
                return result;
            })
            .ToList();

        var patchItems = freshDocument.ОтгружаемыеТовары.Select(Copy).ToList();
        var nextLineNumber = patchItems.Count == 0 ? 1 : patchItems.Max(x => x.LineNumber) + 1;

        foreach (var localItem in shippingOrder.Items)
        {
            var patchItem = patchItems.Single(x => x.LineNumber == localItem.LineNumber);

            if (localItem.FactQuantity > 0)
            {
                patchItem.Количество = localItem.FactQuantity;
                patchItem.КоличествоУпаковок = localItem.FactQuantity;
                patchItem.Действие = ODataEnumMapper.ToODataValue(ShippingOrderAction.Ship);
            }
            else
            {
                patchItem.Количество = localItem.PlanQuantity;
                patchItem.КоличествоУпаковок = localItem.PlanQuantity;
                patchItem.Действие = ODataEnumMapper.ToODataValue(ShippingOrderAction.DoNotShip);
            }

            var notShippedQuantity = localItem.PlanQuantity - localItem.FactQuantity;
            if (localItem.FactQuantity > 0 && notShippedQuantity > 0)
            {
                var notShippedItem = Copy(patchItem);
                notShippedItem.LineNumber = nextLineNumber++;
                notShippedItem.Количество = notShippedQuantity;
                notShippedItem.КоличествоУпаковок = notShippedQuantity;
                notShippedItem.Действие = ODataEnumMapper.ToODataValue(ShippingOrderAction.DoNotShip);
                patchItems.Add(notShippedItem);
            }
        }

        return new PatchBody
        {
            ТоварыПоРаспоряжениям = patchBaseItems,
            ОтгружаемыеТовары = patchItems
        };
    }

    private static bool HasAmbiguousSkuLines(ShippingOrder shippingOrder) =>
        shippingOrder.Items.GroupBy(x => x.StockKeepingUnitId).Any(x => x.Count() > 1)
        || shippingOrder.BaseItems.GroupBy(x => x.StockKeepingUnitId).Any(x => x.Count() > 1);

    private static bool IsTargetItemsState(
        ShippingOrder shippingOrder,
        Document freshDocument)
    {
        var expectedPositiveItems = shippingOrder.Items
            .Where(x => x.FactQuantity > 0)
            .ToList();
        if (freshDocument.ТоварыПоРаспоряжениям.Count != expectedPositiveItems.Count)
            return false;

        foreach (var item in expectedPositiveItems)
        {
            var matchingBaseItems = freshDocument.ТоварыПоРаспоряжениям
                .Where(x => x.Номенклатура_Key == item.StockKeepingUnitId)
                .ToList();
            if (matchingBaseItems.Count != 1
                || matchingBaseItems[0].Количество != item.FactQuantity)
            {
                return false;
            }
        }

        var freshItems = freshDocument.ОтгружаемыеТовары
            .Where(Document.IsRegularShippingItem)
            .ToList();
        var expectedItemCount = shippingOrder.Items.Count
            + shippingOrder.Items.Count(x => x.FactQuantity > 0
                && x.FactQuantity < x.PlanQuantity);
        if (freshItems.Count != expectedItemCount)
            return false;

        foreach (var item in shippingOrder.Items)
        {
            var matchingItems = freshItems
                .Where(x => x.Номенклатура_Key == item.StockKeepingUnitId)
                .ToList();
            var expectedMatchingCount = item.FactQuantity > 0
                && item.FactQuantity < item.PlanQuantity
                    ? 2
                    : 1;
            if (matchingItems.Count != expectedMatchingCount)
                return false;

            var primaryItem = matchingItems.SingleOrDefault(x => x.LineNumber == item.LineNumber);
            var primaryQuantity = item.FactQuantity > 0
                ? item.FactQuantity
                : item.PlanQuantity;
            var primaryAction = item.FactQuantity > 0
                ? ShippingOrderAction.Ship
                : ShippingOrderAction.DoNotShip;
            if (primaryItem is null
                || primaryItem.Количество != primaryQuantity
                || primaryItem.КоличествоУпаковок != primaryQuantity
                || ODataEnumMapper.Parse<ShippingOrderAction>(primaryItem.Действие) != primaryAction)
            {
                return false;
            }

            if (expectedMatchingCount == 2)
            {
                var notShippedItem = matchingItems.Single(x => x.LineNumber != item.LineNumber);
                var notShippedQuantity = item.PlanQuantity - item.FactQuantity;
                if (notShippedItem.Количество != notShippedQuantity
                    || notShippedItem.КоличествоУпаковок != notShippedQuantity
                    || ODataEnumMapper.Parse<ShippingOrderAction>(notShippedItem.Действие) != ShippingOrderAction.DoNotShip)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static Document_РасходныйОрдерНаТовары_ТоварыПоРаспоряжениям Copy(
        Document_РасходныйОрдерНаТовары_ТоварыПоРаспоряжениям source) => new()
        {
            Ref_Key = source.Ref_Key,
            LineNumber = source.LineNumber,
            Номенклатура_Key = source.Номенклатура_Key,
            Количество = source.Количество,
            Распоряжение = source.Распоряжение,
            Распоряжение_Type = source.Распоряжение_Type,
            Характеристика_Key = source.Характеристика_Key,
            Назначение_Key = source.Назначение_Key,
            Серия_Key = source.Серия_Key,
            СтатусУказанияСерий = source.СтатусУказанияСерий
        };

    private static Document_РасходныйОрдерНаТовары_ОтгружаемыеТовары Copy(
        Document_РасходныйОрдерНаТовары_ОтгружаемыеТовары source) => new()
        {
            Ref_Key = source.Ref_Key,
            LineNumber = source.LineNumber,
            Номенклатура_Key = source.Номенклатура_Key,
            Количество = source.Количество,
            КоличествоУпаковок = source.КоличествоУпаковок,
            Действие = source.Действие,
            Характеристика_Key = source.Характеристика_Key,
            Назначение_Key = source.Назначение_Key,
            Серия_Key = source.Серия_Key,
            СтатусУказанияСерий = source.СтатусУказанияСерий,
            ЭтоУпаковочныйЛист = source.ЭтоУпаковочныйЛист,
            Упаковка_Key = source.Упаковка_Key,
            УпаковочныйЛист_Key = source.УпаковочныйЛист_Key,
            УпаковочныйЛистРодитель_Key = source.УпаковочныйЛистРодитель_Key,
            ЭтоСлужебнаяСтрокаПустогоУпаковочногоЛиста = source.ЭтоСлужебнаяСтрокаПустогоУпаковочногоЛиста
        };

    private class PatchBody
    {
        public List<Document_РасходныйОрдерНаТовары_ТоварыПоРаспоряжениям> ТоварыПоРаспоряжениям { get; set; } = [];
        public List<Document_РасходныйОрдерНаТовары_ОтгружаемыеТовары> ОтгружаемыеТовары { get; set; } = [];
    }
}

using Microsoft.Extensions.Logging;
using Wms.Common;
using Wms.Domain;
using Document = Wms.Integration.OneS.Models.Document_РасходныйОрдерНаТовары;

namespace Wms.Integration.OneS.Services;

public class Document_РасходныйОрдерНаТовары_OutboundService(
    OneCClient oneCClient,
    ILogger<Document_РасходныйОрдерНаТовары_OutboundService> logger)
{
    private record StatusOrderCommand(string Статус);

    internal async Task<ServiceResult> StartOrderAsync(Guid orderId, CancellationToken ct) =>
        await SwitchStatusAsync(new StatusOrderCommand("КОтбору"), orderId, ct); // TODO: Магическая строка

    internal async Task<ServiceResult> CompleteOrderAsync(Guid orderId, CancellationToken ct) =>
        await SwitchStatusAsync(new StatusOrderCommand("Отгружен"), orderId, ct); // TODO: Магическая строка

    private async Task<ServiceResult> SwitchStatusAsync(StatusOrderCommand statusOrderCommand, Guid orderId, CancellationToken ct)
    {
        using var scope = logger.BeginScope("SwitchStatus {OrderId} {@StatusOrderCommand}", orderId, statusOrderCommand);

        var patchUri = Document.PatchUri(orderId.ToString());

        var patchResult = await oneCClient.PatchValueAsync<StatusOrderCommand, Document>(patchUri, statusOrderCommand, ct);

        if (!patchResult.IsSuccess)
        {
            return patchResult;
        }

        var postUri = Document.PostDocumentUri(orderId.ToString());

        var postResult = await oneCClient.PostValueAsync(postUri, ct);

        if (!postResult.IsSuccess)
        {
            return postResult;
        }

        return ServiceResult.Success();
    }

    internal async Task<ServiceResult> UpdateDocumentItemsAsync(Guid orderId, List<ShippingOrderItem> shippingOrderItems, CancellationToken ct)
    {
        using var scope = logger.BeginScope("UpdateDocumentItems {OrderId}", orderId);

        var patchItems = shippingOrderItems
            .Select(x => PatchItem.From(x))
            .ToList();

        var patchBaseItems = shippingOrderItems
            .Select(x => PatchBaseItem.From(x))
            .ToList();

        var patchBody = new PatchBody
        {
            ОтгружаемыеТовары = patchItems,
            ТоварыПоРаспоряжениям = patchBaseItems
        };

        var patchUri = Document.PatchUri(orderId.ToString());

        var patchResult = await oneCClient.PatchValueAsync<PatchBody, Document>(patchUri, patchBody, ct);

        if (patchResult is null)
        {
            return ServiceError.Failure<ShippingOrder>("Failed to update external document items");
        }

        return ServiceResult.Success();
    }

    private class PatchBody
    {
        public List<PatchBaseItem> ТоварыПоРаспоряжениям { get; set; } = [];

        public List<PatchItem> ОтгружаемыеТовары { get; set; } = [];
    }

    private class PatchBaseItem
    {
        public Guid Ref_Key { get; set; }
        public int LineNumber { get; set; }
        public Guid Номенклатура_Key { get; set; }
        public double Количество { get; set; }

        public static PatchBaseItem From(ShippingOrderItem orderItem) => new()
        {
            Ref_Key = orderItem.ShippingOrderId,
            LineNumber = orderItem.LineNumber,
            Номенклатура_Key = orderItem.StockKeepingUnitId,
            Количество = orderItem.FactQuantity
        };
    }

    private class PatchItem
    {
        public Guid Ref_Key { get; set; }
        public int LineNumber { get; set; }
        public Guid Номенклатура_Key { get; set; }
        public double КоличествоУпаковок { get; set; }
        public double Количество { get; set; }
        public string? Действие { get; set; }

        public static PatchItem From(ShippingOrderItem orderItem) => new()
        {
            Ref_Key = orderItem.ShippingOrderId,
            LineNumber = orderItem.LineNumber,
            Номенклатура_Key = orderItem.StockKeepingUnitId,
            Количество = orderItem.FactQuantity,
            КоличествоУпаковок = orderItem.FactQuantity,
            Действие = ODataEnumMapper.ToODataValue(orderItem.Action)
        };
    }
}

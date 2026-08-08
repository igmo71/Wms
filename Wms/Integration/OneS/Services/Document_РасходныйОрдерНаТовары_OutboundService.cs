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

    internal async Task<ServiceResult> StartPickingAsync(Guid orderId, CancellationToken ct) =>
        await SwitchStatusAsync(new StatusOrderCommand("КОтбору"), orderId, ct); // TODO: Магическая строка

    internal async Task<ServiceResult> MarkReadyForShipmentAsync(Guid orderId, CancellationToken ct) =>
        await SwitchStatusAsync(new StatusOrderCommand("КОтгрузке"), orderId, ct); // TODO: Магическая строка

    internal async Task<ServiceResult> ShipAsync(Guid orderId, CancellationToken ct) =>
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

    // TODO: Выяснить как в 1С работает с товарами по распоряжениям и отгружаемыми товарами (Отгружать - НеОтгружать)
    // Вероятно нужно добавить строку с НеОтгружать на разницу между План и Факт
    internal async Task<ServiceResult> UpdateDocumentItemsAsync(ShippingOrder shippingOrder, CancellationToken ct)
    {
        using var scope = logger.BeginScope("UpdateDocumentItems {OrderId}", shippingOrder.Id);

        var patchItems = shippingOrder.Items
            .Select(x => PatchItem.From(x))
            .ToList();

        var patchBaseItems = shippingOrder.BaseItems
            .Select(x => PatchBaseItem.From(x))
            .ToList();

        var patchBody = new PatchBody
        {
            ОтгружаемыеТовары = patchItems,
            ТоварыПоРаспоряжениям = patchBaseItems
        };

        var patchUri = Document.PatchUri(shippingOrder.Id.ToString());

        var patchResult = await oneCClient.PatchValueAsync<PatchBody, Document>(patchUri, patchBody, ct);

        if (!patchResult.IsSuccess)
        {
            return patchResult;
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

        public static PatchBaseItem From(ShippingOrderBaseItem orderBaseItem) => new()
        {
            Ref_Key = orderBaseItem.ShippingOrderId,
            LineNumber = orderBaseItem.LineNumber,
            Номенклатура_Key = orderBaseItem.StockKeepingUnitId,
            Количество = orderBaseItem.PlanQuantity
            // Verify whether 1C PATCH of table section requires
            // Распоряжение / Распоряжение_Type to be sent back.
        };
    }

    private class PatchItem
    {
        public Guid Ref_Key { get; set; }
        public int LineNumber { get; set; }
        public Guid Номенклатура_Key { get; set; }
        public double Количество { get; set; }
        public double КоличествоУпаковок { get; set; }
        public string? Действие { get; set; }

        public static PatchItem From(ShippingOrderItem orderItem) => new()
        {
            Ref_Key = orderItem.ShippingOrderId,
            LineNumber = orderItem.LineNumber,
            Номенклатура_Key = orderItem.StockKeepingUnitId,
            Количество = orderItem.FactQuantity,         // Отгружать - НеОтгружать 
            КоличествоУпаковок = orderItem.FactQuantity, // Отгружать - НеОтгружать
            Действие = ODataEnumMapper.ToODataValue(orderItem.Action) // Отгружать - НеОтгружать
        };
    }
}

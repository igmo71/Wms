using Microsoft.Extensions.Logging;
using Wms.Application.Services;
using Wms.Common;
using Wms.Domain;
using Document = Wms.Integration.OneS.Models.Document_РасходныйОрдерНаТовары;

namespace Wms.Integration.OneS.Services;

public class Document_РасходныйОрдерНаТовары_OutboundService(
    OneCClient oneCClient,
    ILogger<Document_РасходныйОрдерНаТовары_OutboundService> logger)
{
    private record StatusOrderCommand(string Статус);

    internal Task<ServiceResult> SetReadyForPickingAsync(Guid orderId, CancellationToken ct) =>
        SwitchStatusAsync("КОтбору", orderId, ct);

    internal Task<ServiceResult> SetReadyForShipmentAsync(Guid orderId, CancellationToken ct) =>
        SwitchStatusAsync("КОтгрузке", orderId, ct);

    internal Task<ServiceResult> SetShippedAsync(Guid orderId, CancellationToken ct) =>
        SwitchStatusAsync("Отгружен", orderId, ct);

    private async Task<ServiceResult> SwitchStatusAsync(string expectedStatus, Guid orderId, CancellationToken ct)
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
            logger.LogError("1C returned an unexpected status. Expected: {ExpectedStatus}, actual: {ActualStatus}", expectedStatus, actualStatus);
            return ServiceResult.Fail(ServiceErrorType.Conflict, $"1C returned an unexpected status. Expected '{expectedStatus}', actual '{actualStatus ?? "<null>"}'.");
        }

        var postUri = Document.PostDocumentUri(orderId.ToString());
        return await oneCClient.PostValueAsync(postUri, ct);
    }

    // TODO: Выяснить как в 1С работает с товарами по распоряжениям и отгружаемыми товарами (Отгружать - НеОтгружать)
    // Вероятно нужно добавить строку с НеОтгружать на разницу между План и Факт
    internal async Task<ServiceResult> UpdateDocumentItemsAsync(ShippingOrder shippingOrder, CancellationToken ct)
    {
        using var scope = logger.BeginScope("UpdateDocumentItems {OrderId}", shippingOrder.Id);
        using var activity = AppTracing.StartActivity("Document_РасходныйОрдерНаТовары.UpdateDocumentItems", nameof(ShippingOrderCommandService));

        var patchItems = shippingOrder.Items.Select(PatchItem.From).ToList();
        var patchBaseItems = shippingOrder.BaseItems.Select(PatchBaseItem.From).ToList();
        var patchBody = new PatchBody
        {
            ОтгружаемыеТовары = patchItems,
            ТоварыПоРаспоряжениям = patchBaseItems
        };

        var patchUri = Document.PatchUri(shippingOrder.Id.ToString());
        var patchResult = await oneCClient.PatchValueAsync<PatchBody, Document>(patchUri, patchBody, ct);

        return patchResult.IsSuccess ? ServiceResult.Success() : patchResult;
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
            Количество = orderItem.FactQuantity, // Отгружать - НеОтгружать
            КоличествоУпаковок = orderItem.FactQuantity, // Отгружать - НеОтгружать
            Действие = ODataEnumMapper.ToODataValue(orderItem.Action) // Отгружать - НеОтгружать
        };
    }
}

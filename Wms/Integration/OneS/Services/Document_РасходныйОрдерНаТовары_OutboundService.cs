using Microsoft.Extensions.Logging;
using Wms.Application.Services;
using Wms.Common;
using Wms.Domain;
using Wms.Integration.OneS.Models;
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

        var patchItems = ;
        var patchBaseItems = ;
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
        public List<Document_РасходныйОрдерНаТовары_ТоварыПоРаспоряжениям> ТоварыПоРаспоряжениям { get; set; } = [];
        public List<Document_РасходныйОрдерНаТовары_ОтгружаемыеТовары> ОтгружаемыеТовары { get; set; } = [];
    }
}

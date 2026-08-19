using Microsoft.Extensions.Logging;
using Wms.Application.Services.ShippingOrders;
using Wms.Common;
using Wms.Domain;
using Document = Wms.Integration.OneS.Models.Document_ПриходныйОрдерНаТовары;

namespace Wms.Integration.OneS.Services;

public class Document_ПриходныйОрдерНаТовары_OutboundService(
    OneCClient oneCClient,
    ILogger<Document_ПриходныйОрдерНаТовары_OutboundService> logger)
{
    private record StatusOrderCommand(string Статус);

    internal Task<OperationResult> SetInReceivingAsync(Guid orderId, CancellationToken ct) =>
        SwitchStatusAsync("ВРаботе", orderId, ct);

    internal Task<OperationResult> SetReceivedAsync(Guid orderId, CancellationToken ct) =>
        SwitchStatusAsync("Принят", orderId, ct);

    private async Task<OperationResult> SwitchStatusAsync(string expectedStatus, Guid orderId, CancellationToken ct)
    {
        using var scope = logger.BeginScope("SwitchStatus {OrderId} {ExpectedStatus}", orderId, expectedStatus);
        using var activity = AppTracing.StartActivity("Document_ПриходныйОрдерНаТовары.SwitchStatus", nameof(ShippingOrderCommandService));

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
            return OperationError.Conflict($"1C returned an unexpected status. Expected '{expectedStatus}', actual '{actualStatus ?? "<null>"}'.");
        }

        var postUri = Document.PostDocumentUri(orderId.ToString());
        return await oneCClient.PostValueAsync(postUri, ct);
    }

    internal async Task<OperationResult> UpdateDocumentItemsAsync(
        Guid orderId,
        IReadOnlyCollection<ReceivingOrderItem> receivingOrderItems,
        CancellationToken ct)
    {
        using var scope = logger.BeginScope("UpdateDocumentItems {OrderId}", orderId);
        using var activity = AppTracing.StartActivity("Document_ПриходныйОрдерНаТовары.UpdateDocumentItems", nameof(ShippingOrderCommandService));

        var patchItems = receivingOrderItems.Select(PatchItem.From).ToList();
        var patchBody = new PatchBody { Товары = patchItems };
        var patchUri = Document.PatchUri(orderId.ToString());
        var patchResult = await oneCClient.PatchValueAsync<PatchBody, Document>(patchUri, patchBody, ct);

        return patchResult.IsSuccess ? OperationResult.Success() : patchResult;
    }

    private class PatchBody
    {
        public List<PatchItem> Товары { get; set; } = [];
    }

    private class PatchItem
    {
        public Guid Ref_Key { get; set; }
        public int LineNumber { get; set; }
        public Guid Номенклатура_Key { get; set; }
        public double КоличествоУпаковок { get; set; }
        public double Количество { get; set; }
        public string? Комментарий { get; set; }

        public static PatchItem From(ReceivingOrderItem orderItem) => new()
        {
            Ref_Key = orderItem.ReceivingOrderId,
            LineNumber = orderItem.LineNumber,
            Номенклатура_Key = orderItem.StockKeepingUnitId,
            Количество = orderItem.FactQuantity,
            КоличествоУпаковок = orderItem.FactQuantity,
            Комментарий = orderItem.Comment
        };
    }
}

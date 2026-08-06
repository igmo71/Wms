using Microsoft.Extensions.Logging;
using Wms.Common;
using Wms.Domain;
using Document = Wms.Integration.OneS.Models.Document_ПриходныйОрдерНаТовары;
using DocumentItem = Wms.Integration.OneS.Models.Document_ПриходныйОрдерНаТовары_Товары;

namespace Wms.Integration.OneS.Services;

public class Document_ПриходныйОрдерНаТовары_OutboundService(
    OneCClient oneCClient,
    ILogger<Document_ПриходныйОрдерНаТовары_OutboundService> logger)
{
    private record StatusOrderCommand(string Статус);

    internal async Task<ServiceResult> StartOrderAsync(Guid orderId, CancellationToken ct) =>
        await SwitchStatusAsync("ВРаботе", orderId, ct);

    internal async Task<ServiceResult> CompleteOrderAsync(Guid orderId, CancellationToken ct) =>
        await SwitchStatusAsync("Принят", orderId, ct);

    private async Task<ServiceResult> SwitchStatusAsync(string status, Guid orderId, CancellationToken ct)
    {
        using var scope = logger.BeginScope("SwitchStatus {OrderId} {Status}", orderId, status);

        var patchUri = Document.PatchUri(orderId.ToString());

        var patchCommand = new StatusOrderCommand(status);

        var patchResult = await oneCClient.PatchValueAsync<StatusOrderCommand, Document>(patchUri, patchCommand, ct);

        if (patchResult is null)
        {
            return ServiceError.Failure<ReceivingOrder>("Failed to patch document status");
        }

        var postUri = Document.PostDocumentUri(orderId.ToString());

        var postSuccess = await oneCClient.PostValueAsync(postUri, ct);

        if (!postSuccess)
        {
            return ServiceError.Failure<ReceivingOrder>("Failed to post document");
        }

        return ServiceResult.Success();
    }

    internal async Task<ServiceResult> UpdateDocumentItemsAsync(Guid orderId, List<ReceivingOrderItem> receivingOrderItems, CancellationToken ct)
    {
        using var scope = logger.BeginScope("UpdateDocumentItems {OrderId}", orderId);

        var documentItems = receivingOrderItems
            .Select(x => DocumentItem.MapFromReceivingOrderItem(x))
            .ToList();

        var patchUri = Document.PatchUri(orderId.ToString());

        var patchResult = await oneCClient.PatchValueAsync<List<DocumentItem>, Document>(patchUri, documentItems, ct);

        if (patchResult is null)
        {
            return ServiceError.Failure<ReceivingOrder>("Failed to patch document items");
        }

        return ServiceResult.Success();
    }
}

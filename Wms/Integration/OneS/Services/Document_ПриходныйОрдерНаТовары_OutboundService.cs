using Microsoft.Extensions.Logging;
using Wms.Domain;
using Document = Wms.Integration.OneS.Models.Document_ПриходныйОрдерНаТовары;
using DocumentItem = Wms.Integration.OneS.Models.Document_ПриходныйОрдерНаТовары_Товары;

namespace Wms.Integration.OneS.Services;

public class Document_ПриходныйОрдерНаТовары_OutboundService(
    OneCClient oneCClient,
    ILogger<Document_ПриходныйОрдерНаТовары_OutboundService> logger)
{
    private record StatusOrderCommand(string Статус);

    internal async Task<ReceivingOrder?> StartOrderAsync(Guid refKey, CancellationToken ct) =>
        await SwitchStatusAsync("ВРаботе", refKey, ct);

    internal async Task<ReceivingOrder?> CompleteOrderAsync(Guid refKey, CancellationToken ct) =>
        await SwitchStatusAsync("Принят", refKey, ct);

    private async Task<ReceivingOrder?> SwitchStatusAsync(string status, Guid refKey, CancellationToken ct)
    {
        using var scope = logger.BeginScope("SwitchStatus {refKey} {Status}", refKey, status);

        var patchUri = Document.PatchUri(refKey.ToString());

        var patchCommand = new StatusOrderCommand(status);

        var patchResult = await oneCClient.PatchValueAsync<StatusOrderCommand, Document>(patchUri, patchCommand, ct);

        if (patchResult is null)
        {
            logger.LogError("Failed to patch document status");
            return null;
        }

        var postUri = Document.PostDocumentUri(refKey.ToString());

        var postSuccessResult = await oneCClient.PostValueAsync(postUri, ct);

        if (!postSuccessResult)
        {
            logger.LogError("Failed to post document");
            return null;
        }

        var result = Document.MapToReceivingOrder(patchResult);

        return result;
    }

    internal async Task<ReceivingOrder?> UpdateDocumentItemsAsync(Guid refKey, List<ReceivingOrderItem> receivingOrderItems, CancellationToken ct)
    {
        using var scope = logger.BeginScope("UpdateDocumentItems {refKey}", refKey);

        var documentItems = receivingOrderItems
            .Select(x => DocumentItem.MapFromReceivingOrderItem(x))
            .ToList();

        var patchUri = Document.PatchUri(refKey.ToString());

        var patchResult = await oneCClient.PatchValueAsync<List<DocumentItem>, Document>(patchUri, documentItems, ct);

        if (patchResult is null)
        {
            logger.LogError("Failed to patch document items");
            return null;
        }

        var result = Document.MapToReceivingOrder(patchResult);

        return result;
    }
}

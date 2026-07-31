using Microsoft.Extensions.Logging;
using Wms.Integration.OneS.Models;

namespace Wms.Integration.OneS.Services;

internal class Document_ПриходныйОрдерНаТовары_OutboundService(
    OneCClient oneCClient,
    ILogger<Document_ПриходныйОрдерНаТовары_OutboundService> logger)
{
    private record StatusOrderCommand(string Статус);

    internal async Task<bool> StartOrderAsync(Guid refKey, CancellationToken ct) =>
        await SwitchStatusAsync("ВРаботе", refKey, ct);

    internal async Task<bool> CompleteOrderAsync(Guid refKey, CancellationToken ct) =>
        await SwitchStatusAsync("Принят", refKey, ct);

    internal async Task<bool> SwitchStatusAsync(string status, Guid orderId, CancellationToken ct)
    {
        var patchUri = Document_ПриходныйОрдерНаТовары.PatchUri(orderId.ToString());

        var patchCommand = new StatusOrderCommand(status);

        var patchResult = await oneCClient.PatchValueAsync<StatusOrderCommand, Document_ПриходныйОрдерНаТовары>(patchUri, patchCommand, ct);

        if (patchResult is null)
        {
            logger.LogError("{Source} Failed to switch to {Status} status {DocId}", nameof(StartOrderAsync), status, orderId);
            return false;
        }

        var postUri = Document_ПриходныйОрдерНаТовары.PostDocumentUri(orderId.ToString());

        var postSuccessResult = await oneCClient.PostValueAsync(postUri, ct);

        if (!postSuccessResult)
        {
            logger.LogError("{Source} Failed to post document {DocId}", nameof(StartOrderAsync), orderId);
            return false;
        }

        return true;
    }
}

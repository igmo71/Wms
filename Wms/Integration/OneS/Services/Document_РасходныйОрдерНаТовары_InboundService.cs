using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Wms.Application.ShippingOrders;
using Wms.Common;
using Wms.Domain;
using Wms.Integration.OneS.Models;
using Document = Wms.Integration.OneS.Models.Document_РасходныйОрдерНаТовары;

namespace Wms.Integration.OneS.Services;

internal class Document_РасходныйОрдерНаТовары_InboundService(
    OneCClient oneCClient,
    ShippingOrderCommandService shippingOrderCommandService,
    IOptions<WmsSettings> options,
    ILogger<Document_РасходныйОрдерНаТовары_InboundService> logger)
{
    private readonly WmsSettings _wmsSettings = options.Value;

    public async Task ImportDocumentAsync(string refKey, CancellationToken ct = default)
    {
        using var scope = logger.BeginScope("ImportDocument {RefKey}", refKey);
        using var activity = AppTracing.StartActivity("Document_РасходныйОрдерНаТовары.Inport", nameof(ShippingOrderCommandService));

        await Task.Delay(TimeSpan.FromSeconds(_wmsSettings.ImportDelay), ct);

        string uri = Document.GetUri(refKey);

        var serviceResult = await oneCClient.GetValueAsync<RootObject<Document>>(uri, ct);

        if (!serviceResult.IsSuccess)
        {
            return;
        }

        var fetchedDocument = serviceResult.Value?.Value?[0];

        if (fetchedDocument is null)
        {
            return;
        }

        logger.LogDebug("Fetched document {@fetchedDocument}", fetchedDocument);

        var unexpectedPreparedItemActions = Document.GetUnexpectedPreparedItemActions(fetchedDocument);
        if (unexpectedPreparedItemActions.Count > 0)
        {
            logger.LogWarning(
                "Prepared shipping order {OrderId} was not imported because its regular item actions differ from PickUp: {@UnexpectedActions}",
                fetchedDocument.Ref_Key,
                unexpectedPreparedItemActions.Select(x => new { x.LineNumber, x.Действие }).ToList());
            return;
        }

        var snapshot = Document.MapToImportSnapshot(fetchedDocument);
        var importResult = await shippingOrderCommandService.ImportOrderAsync(snapshot, ct);
        if (!importResult.IsSuccess)
        {
            logger.LogWarning(
                "Shipping order import was not applied. Order: {OrderId}, Error: {ErrorMessage}",
                snapshot.Id,
                importResult.Error?.Message);
        }
    }

    internal async Task ImportDocumentListAsync(CancellationToken ct)
    {
        throw new NotImplementedException();
    }
}

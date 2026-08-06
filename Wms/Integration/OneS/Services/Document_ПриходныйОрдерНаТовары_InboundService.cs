using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Wms.Application.ReceivingOrders;
using Wms.Common;
using Wms.Domain;
using Wms.Integration.OneS.Models;
using Document = Wms.Integration.OneS.Models.Document_ПриходныйОрдерНаТовары;

namespace Wms.Integration.OneS.Services;

internal class Document_ПриходныйОрдерНаТовары_InboundService(
    OneCClient oneCClient,
    ReceivingOrderCommandService receivingOrderCommandService,
    IOptions<WmsSettings> options,
    ILogger<Document_ПриходныйОрдерНаТовары_InboundService> logger)
{
    private readonly WmsSettings _wmsSettings = options.Value;

    public async Task ImportDocumentAsync(string Ref_Key, CancellationToken ct = default)
    {
        using var scope = logger.BeginScope("ImportDocument {Ref_Key}", Ref_Key);

        await Task.Delay(TimeSpan.FromSeconds(_wmsSettings.ImportDelay), ct);

        var uri = Document.GetUri(Ref_Key);

        var rootObject = await oneCClient.GetValueAsync<RootObject<Document>>(uri, ct);

        var fetchedDocument = rootObject?.Value?[0];

        if (fetchedDocument is null)
        {
            logger.LogError("Failed to fetch document");
            return;
        }

        logger.LogDebug("Fetched document {@fetchedDocument}", fetchedDocument);

        ReceivingOrder order = Document.MapToReceivingOrder(fetchedDocument);

        await receivingOrderCommandService.ImportOrderAsync(order, ct);
    }

    internal async Task ImportListAsync(CancellationToken ct)
    {
        throw new NotImplementedException();
    }
}

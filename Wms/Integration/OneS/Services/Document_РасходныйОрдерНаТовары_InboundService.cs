using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Wms.Application.Services.ShippingOrders;
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

        var uri = Document.GetUri(refKey);

        var serviceResult = await oneCClient.GetValueAsync<RootObject<Document>>(uri, ct);

        if (!serviceResult.IsSuccess)
            return;

        var fetchedDocument = serviceResult.Value?.Value?[0];

        if (fetchedDocument is null)
        {
            return;
        }

        logger.LogDebug("Fetched document {@fetchedDocument}", fetchedDocument);

        ShippingOrder order = Document.MapToShippingOrder(fetchedDocument);

        await shippingOrderCommandService.ImportOrderAsync(order, ct);
    }

    internal async Task ImportDocumentListAsync(CancellationToken ct)
    {
        throw new NotImplementedException();
    }
}

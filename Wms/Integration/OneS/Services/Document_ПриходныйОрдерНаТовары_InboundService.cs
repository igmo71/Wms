using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Wms.Application.Services.ReceivingOrders;
using Wms.Application.Services.ShippingOrders;
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

    public async Task ImportDocumentAsync(string refKey, CancellationToken ct = default)
    {
        using var scope = logger.BeginScope("ImportDocument {RefKey}", refKey);
        using var activity = AppTracing.StartActivity("Document_ПриходныйОрдерНаТовары.Import", nameof(ShippingOrderCommandService));

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

        var snapshot = Document.MapToImportSnapshot(fetchedDocument);
        var importResult = await receivingOrderCommandService.ImportOrderAsync(snapshot, ct);
        if (!importResult.IsSuccess)
        {
            logger.LogWarning(
                "Receiving order import was not applied. Order: {OrderId}, Error: {ErrorMessage}",
                snapshot.Id,
                importResult.Error?.Message);
        }
    }

    internal async Task ImportDocumentListAsync(CancellationToken ct)
    {
        throw new NotImplementedException();
    }
}

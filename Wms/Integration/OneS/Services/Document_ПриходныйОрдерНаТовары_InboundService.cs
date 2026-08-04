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

    public async Task ImportAsync(string Ref_Key, CancellationToken ct = default)
    {
        var source = nameof(ImportAsync);

        if (logger.IsEnabled(LogLevel.Debug))
            logger.LogDebug("{Source} Delay {Ref_Key}", source, Ref_Key);

        await Task.Delay(TimeSpan.FromSeconds(_wmsSettings.ImportDelay), ct);

        if (logger.IsEnabled(LogLevel.Debug))
            logger.LogDebug("{Source} Start {Ref_Key}", source, Ref_Key);

        var fetchedItem = await GetAsync(Ref_Key, ct);

        if (fetchedItem is null)
        {
            logger.LogError("{Source} Not Found {Ref_Key}", source, Ref_Key);
            return;
        }

        if (logger.IsEnabled(LogLevel.Debug))
            logger.LogDebug("{Source} Fetched {@fetchedItem}", source, fetchedItem);

        ReceivingOrder importedOrder = Document.MapToReceivingOrder(fetchedItem);

        await receivingOrderCommandService.CreateOrUpdateImportedOrderAsync(importedOrder, ct);

        if (logger.IsEnabled(LogLevel.Debug))
            logger.LogDebug("{Source} Ok {Ref_Key}", source, Ref_Key);
    }

    private async Task<Document?> GetAsync(string Ref_Key, CancellationToken ct = default)
    {
        var uri = Document.GetUri(Ref_Key);

        var rootObject = await oneCClient.GetValueAsync<RootObject<Document>>(uri, ct);

        var result = rootObject?.Value?[0];

        return result;
    }

    internal async Task ImportListAsync(CancellationToken ct)
    {
        throw new NotImplementedException();
    }
}

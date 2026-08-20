using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Wms.Application.ReceivingOrders;
using Wms.Common;
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

    public async Task<OperationResult> ImportDocumentAsync(string refKey, CancellationToken ct = default)
    {
        using var scope = logger.BeginScope("ImportDocument {RefKey}", refKey);
        using var activity = AppTracing.StartActivity("Document_ПриходныйОрдерНаТовары.Import", nameof(ReceivingOrderCommandService));

        await Task.Delay(TimeSpan.FromSeconds(_wmsSettings.ImportDelay), ct);

        var uri = Document.GetUri(refKey);

        var serviceResult = await oneCClient.GetValueAsync<RootObject<Document>>(uri, ct);

        if (!serviceResult.IsSuccess)
        {
            return serviceResult;
        }

        var fetchedDocument = serviceResult.Value?.Value?[0];

        if (fetchedDocument is null)
        {
            return OperationError.Failure("1С вернула некорректный ответ: приходный ордер отсутствует.");
        }

        logger.LogDebug("Получен документ {@fetchedDocument}", fetchedDocument);

        var snapshot = Document.MapToImportSnapshot(fetchedDocument);
        return await receivingOrderCommandService.ImportOrderAsync(snapshot, ct);
    }
}

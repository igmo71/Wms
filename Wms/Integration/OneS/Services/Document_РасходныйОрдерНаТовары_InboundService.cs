using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Wms.Application.ShippingOrders;
using Wms.Common;
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

    public async Task<OperationResult> ImportDocumentAsync(string refKey, CancellationToken ct = default)
    {
        using var scope = logger.BeginScope("ImportDocument {RefKey}", refKey);
        using var activity = AppTracing.StartActivity("Document_РасходныйОрдерНаТовары.Import", nameof(ShippingOrderCommandService));

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
            return OperationError.Failure("1С вернула некорректный ответ: расходный ордер отсутствует.");
        }

        logger.LogDebug("Получен документ {@fetchedDocument}", fetchedDocument);

        var unexpectedPreparedItemActions = Document.GetUnexpectedPreparedItemActions(fetchedDocument);
        if (unexpectedPreparedItemActions.Count > 0)
        {
            logger.LogWarning("Подготовленный расходный ордер {OrderId} не импортирован: действия обычных строк отличаются от PickUp: {@UnexpectedActions}",
                fetchedDocument.Ref_Key, unexpectedPreparedItemActions.Select(x => new { x.LineNumber, x.Действие }).ToList());
            return OperationError.Conflict(
                "Расходный ордер не импортирован: действие одной или нескольких строк отличается от 'К отбору'.");
        }

        var snapshot = Document.MapToImportSnapshot(fetchedDocument);
        return await shippingOrderCommandService.ImportOrderAsync(snapshot, ct);
    }
}

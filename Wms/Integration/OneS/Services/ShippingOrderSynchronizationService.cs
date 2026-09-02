using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Wms.Application.ShippingOrders;
using Wms.Common;
using Wms.Domain;
using Wms.Integration.OneS.Models;
using Document = Wms.Integration.OneS.Models.Document_РасходныйОрдерНаТовары;

namespace Wms.Integration.OneS.Services;

public sealed class ShippingOrderSynchronizationService(
    OneCClient oneCClient,
    ShippingOrderCommandService shippingOrderCommandService,
    IOptions<WmsSettings> options,
    ILogger<ShippingOrderSynchronizationService> logger)
{
    private readonly WmsSettings _wmsSettings = options.Value;

    internal Task<OperationResult<OrderSynchronizationAssessment>> HandleNotificationAsync(
        string refKey,
        CancellationToken ct = default) =>
        SynchronizeAsync(refKey, allowCreate: true, applyNotificationDelay: true, ct);

    public Task<OperationResult<OrderSynchronizationAssessment>> CheckAsync(
        Guid orderId,
        CancellationToken ct = default) =>
        SynchronizeAsync(orderId.ToString(), allowCreate: false, applyNotificationDelay: false, ct);

    private async Task<OperationResult<OrderSynchronizationAssessment>> SynchronizeAsync(
        string refKey,
        bool allowCreate,
        bool applyNotificationDelay,
        CancellationToken ct)
    {
        using IDisposable? scope = logger.BeginScope(
            "ShippingOrder Synchronize {OrderId}",
            refKey);
        using System.Diagnostics.Activity? activity = AppTracing.StartActivity(
            "ShippingOrder.Synchronize",
            nameof(ShippingOrderSynchronizationService));

        if (applyNotificationDelay)
        {
            await Task.Delay(TimeSpan.FromSeconds(_wmsSettings.ImportDelay), ct);
        }

        OperationResult<RootObject<Document>?> fetchResult =
            await oneCClient.GetValueAsync<RootObject<Document>>(Document.GetUri(refKey), ct);
        if (!fetchResult.IsSuccess)
        {
            return fetchResult.Error!;
        }

        IReadOnlyList<Document>? documents = fetchResult.Value?.Value;
        if (documents is null || documents.Count != 1)
        {
            return OperationError.Failure(
                "1С вернула некорректный ответ: ожидался один расходный ордер.");
        }

        Document document = documents[0];
        logger.LogDebug("Получен документ {@Document}", document);

        return await shippingOrderCommandService.SynchronizeOrderAsync(
            Document.MapToImportSnapshot(document),
            allowCreate,
            ct);
    }
}

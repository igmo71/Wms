using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Wms.Application.ShippingOrders;
using Wms.Common;
using Wms.Domain;

namespace Wms.Integration.OneS.Services;

public sealed class ShippingOrderSynchronizationService(
    IShippingOrderSource orderSource,
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

    public async Task<OperationResult> AcknowledgeAsync(
        Guid orderId,
        string expectedFingerprint,
        string userId,
        CancellationToken ct = default)
    {
        OperationResult<ShippingOrderImportSnapshot> snapshotResult =
            await FetchSnapshotAsync(orderId.ToString(), applyNotificationDelay: false, ct);
        return snapshotResult.IsSuccess
            ? await shippingOrderCommandService.AcknowledgeSynchronizationAsync(
                snapshotResult.Value!, expectedFingerprint, userId, ct)
            : snapshotResult.Error!;
    }

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

        OperationResult<ShippingOrderImportSnapshot> snapshotResult =
            await FetchSnapshotAsync(refKey, applyNotificationDelay, ct);
        return snapshotResult.IsSuccess
            ? await shippingOrderCommandService.SynchronizeOrderAsync(
                snapshotResult.Value!, allowCreate, ct)
            : snapshotResult.Error!;
    }

    private async Task<OperationResult<ShippingOrderImportSnapshot>> FetchSnapshotAsync(
        string refKey,
        bool applyNotificationDelay,
        CancellationToken ct)
    {
        if (applyNotificationDelay)
            await Task.Delay(TimeSpan.FromSeconds(_wmsSettings.ImportDelay), ct);

        return Guid.TryParse(refKey, out Guid orderId)
            ? await orderSource.GetSnapshotAsync(orderId, ct)
            : OperationError.Invalid("Некорректный идентификатор расходного ордера 1С.");
    }
}

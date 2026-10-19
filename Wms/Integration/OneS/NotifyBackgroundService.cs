using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Wms.Application.Receiving;
using Wms.Application.ShippingOrders;
using Wms.Common;
using Wms.Integration.OneS.Models;
using Wms.Integration.OneS.Services;

namespace Wms.Integration.OneS;

internal class NotifyBackgroundService(
    NotifyChannel notifyChannel,
    IServiceScopeFactory scopeFactory,
    IOptions<WmsSettings> options,
    ILogger<NotifyBackgroundService> logger) : BackgroundService
{
    private readonly WmsSettings _wmsSettings = options.Value;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await foreach (var notifyRecord in notifyChannel.Reader.ReadAllAsync(ct))
        {
            try
            {
                OperationResult result = await DispatchNotificationAsync(notifyRecord, ct);
                if (!result.IsSuccess)
                {
                    logger.LogError("Уведомление 1С не обработано. Тип: {Type}, RefKey: {RefKey}, ошибка: {Error}",
                        notifyRecord.Type, notifyRecord.Ref_Key, result.Error?.Message);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Непредвиденная ошибка обработки уведомления 1С. Тип: {Type}, RefKey: {RefKey}",
                    notifyRecord.Type, notifyRecord.Ref_Key);
            }
        }
    }

    private async Task<OperationResult> DispatchNotificationAsync(
        NotifyRecord notifyRecord,
        CancellationToken ct)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        IServiceProvider services = scope.ServiceProvider;

        return notifyRecord.Type switch
        {
            nameof(Catalog_ЗоныДоставки) =>
                await services.GetRequiredService<Catalog_ЗоныДоставки_Service>()
                    .ImportAsync(notifyRecord.Ref_Key, ct),
            nameof(Catalog_Номенклатура) =>
                await services.GetRequiredService<Catalog_Номенклатура_Service>()
                    .ImportAsync(notifyRecord.Ref_Key, ct),
            nameof(Catalog_Партнеры) =>
                await services.GetRequiredService<Catalog_Партнеры_Service>()
                    .ImportAsync(notifyRecord.Ref_Key, ct),
            nameof(Catalog_Склады) =>
                await services.GetRequiredService<Catalog_Склады_Service>()
                    .ImportAsync(notifyRecord.Ref_Key, ct),
            nameof(Catalog_СтруктураПредприятия) =>
                await services.GetRequiredService<Catalog_СтруктураПредприятия_Service>()
                    .ImportAsync(notifyRecord.Ref_Key, ct),
            nameof(Catalog_УпаковкиЕдиницыИзмерения) =>
                await services.GetRequiredService<Catalog_УпаковкиЕдиницыИзмерения_Service>()
                    .ImportAsync(notifyRecord.Ref_Key, ct),
            nameof(Catalog_ФизическиеЛица) =>
                await services.GetRequiredService<Catalog_ФизическиеЛица_Service>()
                    .ImportAsync(notifyRecord.Ref_Key, ct),
            "Document_ПриходныйОрдерНаТовары" =>
                await SynchronizeReceivingDocumentAsync(services, notifyRecord.Ref_Key, ct),
            nameof(Document_РасходныйОрдерНаТовары) =>
                await ImportShippingOrderAsync(services, notifyRecord.Ref_Key, ct),
            nameof(InformationRegister_ШтрихкодыНоменклатуры) =>
                await services.GetRequiredService<InformationRegister_ШтрихкодыНоменклатуры_Service>()
                    .ImportAsync(notifyRecord.Ref_Key, ct),
            _ => OperationError.Invalid($"Неподдерживаемый тип уведомления 1С: {notifyRecord.Type}.")
        };
    }

    private async Task<OperationResult> SynchronizeReceivingDocumentAsync(
        IServiceProvider services,
        string refKey,
        CancellationToken ct)
    {
        OperationResult<Guid> refKeyResult = await ParseDocumentIdAfterDelayAsync(
            refKey,
            "приходного",
            ct);
        if (!refKeyResult.IsSuccess)
            return refKeyResult.Error!;

        var result = await services.GetRequiredService<SynchronizeReceivingSource>()
            .ExecuteAsync(refKeyResult.Value, ct);
        return result.IsSuccess ? OperationResult.Success() : result.Error!;
    }

    private async Task<OperationResult> ImportShippingOrderAsync(
        IServiceProvider services,
        string refKey,
        CancellationToken ct)
    {
        OperationResult<Guid> orderIdResult = await ParseDocumentIdAfterDelayAsync(
            refKey,
            "расходного",
            ct);
        return orderIdResult.IsSuccess
            ? await services.GetRequiredService<ShippingOrderSynchronizationService>()
                .ImportNotificationAsync(orderIdResult.Value, ct)
            : orderIdResult.Error!;
    }

    private async Task<OperationResult<Guid>> ParseDocumentIdAfterDelayAsync(
        string refKey,
        string orderKind,
        CancellationToken ct)
    {
        await Task.Delay(TimeSpan.FromSeconds(_wmsSettings.ImportDelay), ct);
        return Guid.TryParse(refKey, out Guid orderId)
            ? orderId
            : OperationError.Invalid($"Некорректный идентификатор {orderKind} ордера 1С.");
    }
}

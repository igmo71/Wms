using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Wms.Common;
using Wms.Integration.OneS.Models;
using Wms.Integration.OneS.Services;

namespace Wms.Integration.OneS;

internal class NotifyBackgroundService(
    NotifyChannel notifyChannel,
    IServiceScopeFactory scopeFactory,
    ILogger<NotifyBackgroundService> logger) : BackgroundService
{
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
            nameof(Document_ПриходныйОрдерНаТовары) =>
                await services.GetRequiredService<Document_ПриходныйОрдерНаТовары_SynchronizationService>()
                    .HandleNotificationAsync(notifyRecord.Ref_Key, ct),
            nameof(Document_РасходныйОрдерНаТовары) =>
                await services.GetRequiredService<Document_РасходныйОрдерНаТовары_SynchronizationService>()
                    .HandleNotificationAsync(notifyRecord.Ref_Key, ct),
            nameof(InformationRegister_ШтрихкодыНоменклатуры) =>
                await services.GetRequiredService<InformationRegister_ШтрихкодыНоменклатуры_Service>()
                    .ImportAsync(notifyRecord.Ref_Key, ct),
            _ => OperationError.Invalid($"Неподдерживаемый тип уведомления 1С: {notifyRecord.Type}.")
        };
    }
}

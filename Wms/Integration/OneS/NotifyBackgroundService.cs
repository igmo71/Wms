using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Wms.Integration.OneS.Models;
using Wms.Integration.OneS.Services;

namespace Wms.Integration.OneS;

public class NotifyBackgroundService(
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
                await DispatchNotification(notifyRecord, ct);
            }
            catch (Exception ex)
            {
                if (logger.IsEnabled(LogLevel.Error))
                    logger.LogError(ex, "{Source} {Exception}", nameof(ExecuteAsync), ex.Message);
                //throw;
            }
        }
    }

    private async Task DispatchNotification(NotifyRecord notifyRecord, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();

        switch (notifyRecord.Type)
        {
            case nameof(Catalog_Номенклатура):
                {
                    var service = scope.ServiceProvider.GetService<Catalog_Номенклатура_Service>();
                    if (service is not null)
                        await service.ImportAsync(notifyRecord.Ref_Key, ct);
                    break;
                }
            case nameof(Catalog_Склады):
                {
                    var service = scope.ServiceProvider.GetService<Catalog_Склады_Service>();
                    if (service is not null)
                        await service.ImportAsync(notifyRecord.Ref_Key, ct);
                    break;
                }
            case nameof(Catalog_Партнеры):
                {
                    var service = scope.ServiceProvider.GetService<Catalog_Партнеры_Service>();
                    if (service is not null)
                        await service.ImportAsync(notifyRecord.Ref_Key, ct);
                    break;
                }
            case nameof(Catalog_УпаковкиЕдиницыИзмерения):
                {
                    var service = scope.ServiceProvider.GetService<Catalog_УпаковкиЕдиницыИзмерения_Service>();
                    if (service is not null)
                        await service.ImportAsync(notifyRecord.Ref_Key, ct);
                    break;
                }
            case nameof(Document_ПриходныйОрдерНаТовары):
                {
                    var service = scope.ServiceProvider.GetService<Document_ПриходныйОрдерНаТовары_InboundService>();
                    if (service is not null)
                        await service.ImportDocumentAsync(notifyRecord.Ref_Key, ct);
                    break;
                }

            case nameof(Document_РасходныйОрдерНаТовары):
                {
                    var service = scope.ServiceProvider.GetService<Document_РасходныйОрдерНаТовары_InboundService>();
                    if (service is not null)
                        await service.ImportDocumentAsync(notifyRecord.Ref_Key, ct);
                    break;
                }
            case nameof(InformationRegister_ШтрихкодыНоменклатуры):
                {
                    var service = scope.ServiceProvider.GetService<InformationRegister_ШтрихкодыНоменклатуры_Service>();
                    if (service is not null)
                        await service.ImportAsync(notifyRecord.Ref_Key, ct);
                    break;
                }
            default:
                logger.LogError("{Source} Unsupported NotifyRecord {@NotifyRecord}", nameof(DispatchNotification), notifyRecord);
                break;
        }
    }
}

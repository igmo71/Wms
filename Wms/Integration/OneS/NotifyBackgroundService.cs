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
                    logger.LogError(ex, "{Source} - Error dispatching app event from channel", nameof(ExecuteAsync));
                throw;
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
            case nameof(Catalog_УпаковкиЕдиницыИзмерения):
                {
                    var service = scope.ServiceProvider.GetService<Catalog_УпаковкиЕдиницыИзмерения_Service>();
                    if (service is not null)
                        await service.ImportAsync(notifyRecord.Ref_Key, ct);
                    break;
                }
            default:
                logger.LogError("{Source} Unsupported NotifyRecord {@}", nameof(DispatchNotification), notifyRecord);
                break;
        }
    }
}

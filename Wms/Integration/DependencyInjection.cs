using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Headers;
using System.Net.Mime;
using System.Text;
using Wms.Integration.OneS;
using Wms.Integration.OneS.Services;
using Wms.Application.ReceivingOrders;
using Wms.Application.ShippingOrders;

namespace Wms.Integration;

public static class DependencyInjection
{
    public static IServiceCollection AddIntegrationServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpClient<OneCClient>(client =>
        {
            var config = configuration.GetSection(OneCClientConfig.Section).Get<OneCClientConfig>()
                ?? throw new InvalidOperationException($"Раздел конфигурации '{OneCClientConfig.Section}' отсутствует.");

            client.BaseAddress = new Uri(config.BaseAddress);

            var authToken = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{config.Username}:{config.Password}"));

            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authToken);

            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(MediaTypeNames.Application.Json));
        });

        services.AddScoped<Catalog_ЗоныДоставки_Service>();
        services.AddScoped<Catalog_Номенклатура_Service>();
        services.AddScoped<Catalog_Партнеры_Service>();
        services.AddScoped<Catalog_Склады_Service>();
        services.AddScoped<Catalog_СтруктураПредприятия_Service>();
        services.AddScoped<Catalog_ФизическиеЛица_Service>();
        services.AddScoped<Catalog_УпаковкиЕдиницыИзмерения_Service>();
        services.AddScoped<Document_ПриходныйОрдерНаТовары_SynchronizationService>();
        services.AddScoped<IReceivingOrderSource, Document_ПриходныйОрдерНаТовары_InboundService>();
        services.AddScoped<Document_ПриходныйОрдерНаТовары_OutboundService>();
        services.AddScoped<Document_РасходныйОрдерНаТовары_SynchronizationService>();
        services.AddScoped<IShippingOrderSource, Document_РасходныйОрдерНаТовары_InboundService>();
        services.AddScoped<Document_РасходныйОрдерНаТовары_OutboundService>();
        services.AddScoped<InformationRegister_ШтрихкодыНоменклатуры_Service>();

        return services;
    }

    public static IServiceCollection AddNotificationServices(this IServiceCollection services)
    {

        services.AddSingleton<NotifyChannel>();

        services.AddHostedService<NotifyBackgroundService>();

        return services;
    }
}

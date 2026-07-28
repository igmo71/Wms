using System.Net.Http.Headers;
using System.Net.Mime;
using System.Text;
using Wms.WebApp.Integration.OneS;
using Wms.WebApp.Integration.OneS.Services;

namespace Wms.WebApp.Integration;

public static class DependencyInjection
{
    public static IServiceCollection AddIntegrationServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpClient<OneCClient>(client =>
        {
            var config = configuration.GetSection(OneCClientConfig.Section).Get<OneCClientConfig>()
                ?? throw new InvalidOperationException($"Configuration section '{OneCClientConfig.Section}' is missing.");

            client.BaseAddress = new Uri(config.BaseAddress);

            var authToken = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{config.Username}:{config.Password}"));

            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authToken);

            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(MediaTypeNames.Application.Json));
        });

        services.AddSingleton<NotifyChannel>();

        services.AddHostedService<NotifyBackgroundService>();

        services.AddScoped<Catalog_УпаковкиЕдиницыИзмерения_Service>();

        return services;
    }
}

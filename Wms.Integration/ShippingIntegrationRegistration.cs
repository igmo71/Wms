using System.Net.Http.Headers;
using System.Net.Mime;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Wms.Application.Picking;
using Wms.Integration.OneS;

namespace Wms.Integration;

public static class ShippingIntegrationRegistration
{
    public static IServiceCollection AddShippingSourceReader(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IShippingStoredSnapshotParser, ShippingStoredSnapshotParser>();
        services.AddHttpClient<IShippingSourceReader, ShippingSourceReader>(
            client => ConfigureClient(client, configuration));
        services.AddHttpClient<IShippingSourceWriter, ShippingSourceWriter>(
            client => ConfigureClient(client, configuration));
        return services;
    }

    private static void ConfigureClient(HttpClient client, IConfiguration configuration)
    {
        var section = configuration.GetSection("OneCClient");
        var baseAddress = section["BaseAddress"]
            ?? throw new InvalidOperationException("Адрес 1С не настроен.");
        var username = section["Username"]
            ?? throw new InvalidOperationException("Имя пользователя 1С не настроено.");
        var password = section["Password"]
            ?? throw new InvalidOperationException("Пароль 1С не настроен.");

        client.BaseAddress = new Uri(baseAddress);
        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue(MediaTypeNames.Application.Json));
    }
}

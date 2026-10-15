using System.Net.Http.Headers;
using System.Net.Mime;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Wms.Application.Receiving;
using Wms.Integration.OneS;

namespace Wms.Integration;

public static class ReceivingIntegrationRegistration
{
    public static IServiceCollection AddReceivingSourceReader(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpClient<IReceivingSourceReader, ReceivingSourceReader>(client =>
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
        });
        services.AddSingleton<IReceivingSourceParser, ReceivingSourceParser>();

        return services;
    }
}

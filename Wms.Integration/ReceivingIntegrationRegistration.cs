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
        services.AddHttpClient<IReceivingSourceReader, ReceivingSourceReader>(
            client => OneCClientConfiguration.Configure(client, configuration));
        services.AddSingleton<IReceivingSourceParser, ReceivingSourceParser>();

        return services;
    }
}

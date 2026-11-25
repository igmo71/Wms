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
            client => OneCClientConfiguration.Configure(client, configuration));
        services.AddHttpClient<IShippingSourceWriter, ShippingSourceWriter>(
            client => OneCClientConfiguration.Configure(client, configuration));
        return services;
    }
}

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Wms.Application.Reports;
using Wms.Integration.OneS;

namespace Wms.Integration;

public static class StockReconciliationIntegrationRegistration
{
    public static IServiceCollection AddOneCStockBalanceReader(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpClient<IOneCStockBalanceReader, OneCStockBalanceReader>(
            client => OneCClientConfiguration.Configure(client, configuration));
        return services;
    }
}

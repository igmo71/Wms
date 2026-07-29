using Microsoft.Extensions.DependencyInjection;

namespace Wms.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddScoped<StockKeepingUnitService>();
        services.AddScoped<StockKeepingUnitService>();
        services.AddScoped<UnitOfMeasureService>();
        services.AddScoped<WarehouseService>();
        services.AddScoped<ZoneService>();

        return services;
    }
}

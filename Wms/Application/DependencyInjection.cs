using Microsoft.Extensions.DependencyInjection;
using Wms.Application.ReceivingOrders;

namespace Wms.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddScoped<ReceivingOrderCommandService>();
        services.AddScoped<ReceivingOrderQueryService>();
        services.AddScoped<SkuBarcodeService>();
        services.AddScoped<StockKeepingUnitService>();
        services.AddScoped<StorageLocationService>();
        services.AddScoped<UnitOfMeasureService>();
        services.AddScoped<WarehouseService>();
        services.AddScoped<ZoneService>();

        return services;
    }
}

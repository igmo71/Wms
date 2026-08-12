using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Wms.Application.Services;
using Wms.Common;

namespace Wms.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<BalanceAndTurnoverService>();
        services.AddScoped<DeliveryDirectionService>();
        services.AddScoped<InventoryCountCommandService>();
        services.AddScoped<InventoryCountQueryService>();
        services.AddScoped<InventoryBalanceService>();
        services.AddScoped<InventoryMovementService>();
        services.AddScoped<InventoryTurnoverService>();
        services.AddScoped<PickingCommandService>();
        services.AddScoped<PickingQueryService>();
        services.AddScoped<ReceivingOrderCommandService>();
        services.AddScoped<ReceivingOrderQueryService>();
        services.AddScoped<ShippingOrderCommandService>();
        services.AddScoped<ShippingOrderQueryService>();
        services.AddScoped<SkuBarcodeService>();
        services.AddScoped<StockKeepingUnitService>();
        services.AddScoped<StorageLocationService>();
        services.AddScoped<UnitOfMeasureService>();
        services.AddScoped<WarehouseImportService>();
        services.AddScoped<WarehouseService>();
        services.AddScoped<ZoneService>();

        services.Configure<WmsSettings>(configuration.GetSection(nameof(WmsSettings)));

        return services;
    }
}

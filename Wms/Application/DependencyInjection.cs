using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Wms.Application.DeliveryDirections;
using Wms.Application.Individuals;
using Wms.Application.Inventory.Balances;
using Wms.Application.Inventory.Counts;
using Wms.Application.Inventory.Movements;
using Wms.Application.Inventory.Transfers;
using Wms.Application.Inventory.Turnovers;
using Wms.Application.MobileCommands;
using Wms.Application.OrganizationalUnits;
using Wms.Application.Parties;
using Wms.Application.Partners;
using Wms.Application.ReceivingOrders;
using Wms.Application.Reports.EmployeePerformance;
using Wms.Application.ShippingOrders;
using Wms.Application.SkuBarcodes;
using Wms.Application.StockKeepingUnits;
using Wms.Application.StorageLocations;
using Wms.Application.UnitsOfMeasure;
using Wms.Application.Users;
using Wms.Application.Warehouses;
using Wms.Application.Zones;
using Wms.Common;

namespace Wms.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ApplicationUserQueryService>();
        services.AddScoped<InventoryPostingService>();
        services.AddScoped<DeliveryDirectionService>();
        services.AddScoped<EmployeePerformanceReportService>();
        services.AddScoped<IndividualService>();
        services.AddScoped<InventoryCountCommandService>();
        services.AddScoped<MobileInventoryCountCommandService>();
        services.AddScoped<InventoryCountQueryService>();
        services.AddScoped<InventoryBalanceQueryService>();
        services.AddScoped<InventoryMovementQueryService>();
        services.AddScoped<InventoryTurnoverQueryService>();
        services.AddScoped<MobileCommandExecutor>();
        services.AddScoped<OrganizationalUnitService>();
        services.AddScoped<PickingCommandService>();
        services.AddScoped<PartyQueryService>();
        services.AddScoped<PartnerService>();
        services.AddScoped<PickingQueryService>();
        services.AddScoped<PutawayCommandService>();
        services.AddScoped<PutawayQueryService>();
        services.AddScoped<ReceivingOrderCommandService>();
        services.AddScoped<MobileReceivingOrderCommandService>();
        services.AddScoped<MobileReceivingOrderQueryService>();
        services.AddScoped<ReceivingOrderQueryService>();
        services.AddScoped<ShippingOrderCommandService>();
        services.AddScoped<ShippingOrderQueryService>();
        services.AddScoped<SkuBarcodeService>();
        services.AddScoped<StockKeepingUnitService>();
        services.AddScoped<InventoryTransferCommandService>();
        services.AddScoped<MobileInventoryTransferCommandService>();
        services.AddScoped<InventoryTransferQueryService>();
        services.AddScoped<StorageLocationCommandService>();
        services.AddScoped<StorageLocationLockCommandService>();
        services.AddScoped<StorageLocationQueryService>();
        services.AddScoped<UnitOfMeasureService>();
        services.AddScoped<WarehouseService>();
        services.AddScoped<ZoneCommandService>();
        services.AddScoped<ZoneQueryService>();

        services.Configure<WmsSettings>(configuration.GetSection(nameof(WmsSettings)));

        return services;
    }
}

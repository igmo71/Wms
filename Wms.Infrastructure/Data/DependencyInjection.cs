using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Wms.Application.Authorization;
using Wms.Application.CommandExecution;
using Wms.Application.Inventory;
using Wms.Application.Receiving;
using Wms.Application.Reports;
using Wms.Application.StockKeepingUnits;

namespace Wms.Data;

public static class DependencyInjection
{
    public static IServiceCollection AddApplicationDbContext(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Строка подключения 'DefaultConnection' не найдена.");

        services.AddDbContextFactory<ApplicationDbContext>(options =>
        {
            options.UseSqlServer(connectionString);
            options.EnableSensitiveDataLogging();
            options.EnableDetailedErrors();
        });

        services.AddDatabaseDeveloperPageExceptionFilter();
        services.AddScoped<IWarehousePermissionStore, WarehousePermissionStore>();
        services.AddScoped<WarehousePermission>();
        services.AddScoped<ICommandExecutionStore, CommandExecutionStore>();
        services.AddScoped<CommandExecutor>();
        services.AddScoped<IInventoryPostingStore, InventoryPostingStore>();
        services.AddScoped<PostInventoryMovement.Handler>();
        services.AddScoped<IReceivingSourceStore, ReceivingSourceStore>();
        services.AddScoped<SynchronizeReceivingSource>();
        services.AddScoped<IReceivingOrderStore, ReceivingOrderStore>();
        services.AddScoped<CreateReceivingOrder>();
        services.AddScoped<GetReceivingSources>();
        services.AddScoped<IReceivingSkuStore, ReceivingSkuStore>();
        services.AddScoped<SearchReceivingSku>();
        services.AddScoped<AddReceivingOrderSku>();
        services.AddScoped<IReceivingWorkStore, ReceivingWorkStore>();
        services.AddScoped<IReceivingWorkQueryStore, ReceivingWorkQueryStore>();
        services.AddScoped<JoinReceivingCycle>();
        services.AddScoped<OpenReceivingLpn>();
        services.AddScoped<EditReceivingLpn>();
        services.AddScoped<CancelReceivingLpn>();
        services.AddScoped<ConfirmZeroReceived>();
        services.AddScoped<CloseReceivingLpn>();
        services.AddScoped<PutAwayReceivingLpn>();
        services.AddScoped<CorrectReceivingLpnQuantity>();
        services.AddScoped<IReceivingLpnDeletionStore, ReceivingLpnDeletionStore>();
        services.AddScoped<PreviewReceivingLpnDeletion>();
        services.AddScoped<DeleteReceivingLpn>();
        services.AddScoped<IExpirationTrackingStore, ExpirationTrackingStore>();
        services.AddScoped<SetExpirationTracking>();
        services.AddScoped<IUndatedExpirationStockStore, UndatedExpirationStockStore>();
        services.AddScoped<GetUndatedExpirationStock>();
        services.AddSingleton(TimeProvider.System);

        return services;
    }
}

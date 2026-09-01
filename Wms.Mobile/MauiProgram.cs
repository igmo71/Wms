using BarcodeScanning;
using Microsoft.Extensions.Logging;
using Wms.Mobile.Scanning;
using Wms.Mobile.Services;

namespace Wms.Mobile;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        builder
            .UseMauiApp<App>()
            .UseBarcodeScanning()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        builder.Services.AddSingleton<AppShell>();
        builder.Services.AddTransient<MainPage>();
        builder.Services.AddTransient<InventoryTransferPage>();
        builder.Services.AddTransient<InventoryCountPage>();
        builder.Services.AddTransient<ReceivingOrderPage>();
        builder.Services.AddTransient<ReceivingOrderReceivingPage>();
        builder.Services.AddTransient<ReceivingOrderPutawayPage>();
        builder.Services.AddTransient<ReceivingOrderPutawayMovementPage>();
        builder.Services.AddTransient<ShippingOrderPage>();
        builder.Services.AddTransient<ShippingOrderPickingPage>();
        builder.Services.AddTransient<ShippingOrderShippingPage>();
        builder.Services.AddTransient<ScannerDiagnosticsPage>();

        builder.Services.AddSingleton<AndroidIntentBarcodeScanner>();
        builder.Services.AddSingleton<ILifecycleBarcodeScanner>(serviceProvider =>
            serviceProvider.GetRequiredService<AndroidIntentBarcodeScanner>());
        builder.Services.AddSingleton<CameraBarcodeScanner>();
        builder.Services.AddSingleton<ICameraBarcodeScanner>(serviceProvider =>
            serviceProvider.GetRequiredService<CameraBarcodeScanner>());
        builder.Services.AddSingleton<IOperationalBarcodeScanner, OperationalBarcodeScanner>();

        builder.Services.AddSingleton<ISecureStorage>(SecureStorage.Default);
        builder.Services.AddSingleton<IMobileSessionStore, SecureStorageMobileSessionStore>();
        builder.Services.AddSingleton<MobileAuthenticationHandler>();
        builder.Services.AddSingleton(MobileApiSettings.Load());
        builder.Services.AddSingleton(serviceProvider => new HttpClient(
            serviceProvider.GetRequiredService<MobileAuthenticationHandler>())
        {
            BaseAddress = new Uri(
                serviceProvider.GetRequiredService<MobileApiSettings>().BaseAddress)
        });
        builder.Services.AddSingleton(serviceProvider => new MobileApiClient(
            serviceProvider.GetRequiredService<HttpClient>(),
            serviceProvider.GetRequiredService<IMobileSessionStore>()));

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}

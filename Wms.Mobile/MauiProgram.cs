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

        Microsoft.Maui.Handlers.EntryHandler.Mapper.AppendToMapping("ExplicitKeyboardInput", (handler, view) =>
        {
#if ANDROID
            // Native focus restoration must not open the keyboard.
            handler.PlatformView.ShowSoftInputOnFocus = false;
            handler.PlatformView.Click -= OnAndroidEntryClicked;
            handler.PlatformView.Click += OnAndroidEntryClicked;
#endif
            if (view is Entry entry)
            {
                entry.Completed -= OnEntryCompleted;
                entry.Completed += OnEntryCompleted;
            }
        });

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
        builder.Services.AddTransient(serviceProvider => new ReceivingOrderReceivingProcess(
            serviceProvider.GetRequiredService<MobileReceivingOrderClient>()));
        builder.Services.AddTransient<ReceivingOrderPutawayPage>();
        builder.Services.AddTransient<ReceivingOrderPutawayMovementPage>();
        builder.Services.AddTransient<LicensePlateNumberPutawayPage>();
        builder.Services.AddTransient<ShippingOrderPage>();
        builder.Services.AddTransient<ShippingOrderPickingPage>();
        builder.Services.AddTransient<ShippingOrderPickingMovementPage>();
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
        builder.Services.AddSingleton(serviceProvider => new MobileApiTransport(
            serviceProvider.GetRequiredService<HttpClient>()));
        builder.Services.AddSingleton(serviceProvider => new MobileIdentityClient(
            serviceProvider.GetRequiredService<MobileApiTransport>(),
            serviceProvider.GetRequiredService<IMobileSessionStore>()));
        builder.Services.AddSingleton(serviceProvider => new MobileReferenceDataClient(
            serviceProvider.GetRequiredService<MobileApiTransport>()));
        builder.Services.AddSingleton(serviceProvider => new MobileReceivingOrderClient(
            serviceProvider.GetRequiredService<MobileApiTransport>()));
        builder.Services.AddSingleton(serviceProvider => new MobileLicensePlateNumberPutawayClient(
            serviceProvider.GetRequiredService<MobileApiTransport>()));
        builder.Services.AddSingleton(serviceProvider => new MobileShippingOrderClient(
            serviceProvider.GetRequiredService<MobileApiTransport>()));
        builder.Services.AddSingleton(serviceProvider => new MobileInventoryTransferClient(
            serviceProvider.GetRequiredService<MobileApiTransport>()));
        builder.Services.AddSingleton(serviceProvider => new MobileInventoryCountClient(
            serviceProvider.GetRequiredService<MobileApiTransport>()));

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }

#if ANDROID
    private static void OnAndroidEntryClicked(object? sender, EventArgs e)
    {
        if (sender is not Android.Widget.EditText { Enabled: true } field
            || !field.RequestFocus())
            return;

        if (field.Context?.GetSystemService(Android.Content.Context.InputMethodService)
            is Android.Views.InputMethods.InputMethodManager keyboard)
        {
            keyboard.ShowSoftInput(field, Android.Views.InputMethods.ShowFlags.Implicit);
        }
    }
#endif

    private static async void OnEntryCompleted(object? sender, EventArgs e)
    {
        if (sender is Entry entry)
        {
            await entry.HideSoftInputAsync(CancellationToken.None);
            entry.Unfocus();
        }
    }
}

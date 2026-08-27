using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Microsoft.Extensions.DependencyInjection;
using Wms.Mobile.Scanning;

namespace Wms.Mobile;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, WindowSoftInputMode = SoftInput.AdjustResize, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnResume()
    {
        base.OnResume();
        IPlatformApplication.Current?.Services
            .GetService<ILifecycleBarcodeScanner>()?
            .Start();
    }

    protected override void OnPause()
    {
        IPlatformApplication.Current?.Services
            .GetService<ILifecycleBarcodeScanner>()?
            .Stop();
        base.OnPause();
    }
}

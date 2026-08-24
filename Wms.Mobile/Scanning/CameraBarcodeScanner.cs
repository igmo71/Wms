namespace Wms.Mobile.Scanning;

public sealed class CameraBarcodeScanner : ICameraBarcodeScanner
{
    public BarcodeScanSource Source => BarcodeScanSource.Camera;

    public bool IsAvailable
    {
        get
        {
#if ANDROID
            return Android.App.Application.Context.PackageManager?
                .HasSystemFeature(Android.Content.PM.PackageManager.FeatureCameraAny) == true;
#else
            return false;
#endif
        }
    }

    public event EventHandler<BarcodeScanEvent>? ScanReceived;

    public bool TryAccept(string? value, string? symbology)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        ScanReceived?.Invoke(
            this,
            new BarcodeScanEvent(
                value,
                symbology,
                DateTimeOffset.UtcNow,
                Source,
                new Dictionary<string, string>
                {
                    ["engine"] = "Google ML Kit"
                }));

        return true;
    }
}

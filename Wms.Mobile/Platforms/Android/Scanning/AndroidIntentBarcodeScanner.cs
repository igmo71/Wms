using System.Globalization;
using Android.Content;
using Android.OS;

namespace Wms.Mobile.Scanning;

public sealed class AndroidIntentBarcodeScanner : ILifecycleBarcodeScanner
{
    private const string ScannerApiClassName = "android.device.ScanManager";

    private static readonly IReadOnlyDictionary<string, string> TechnicalDetails =
        new Dictionary<string, string>
        {
            ["profile"] = UrovoIntentScannerProfile.Name,
            ["action"] = UrovoIntentScannerProfile.Action,
            ["stringExtra"] = UrovoIntentScannerProfile.BarcodeStringExtra
        };

    private readonly Context _context;
    private readonly ScannerBroadcastReceiver _receiver;
    private bool _registered;

    public AndroidIntentBarcodeScanner()
    {
        _context = Android.App.Application.Context;
        _receiver = new ScannerBroadcastReceiver(this);
    }

    public BarcodeScanSource Source => BarcodeScanSource.EmbeddedScanner;

    public bool IsAvailable
    {
        get
        {
            try
            {
                using var scannerClass = Java.Lang.Class.ForName(ScannerApiClassName);
                return scannerClass is not null;
            }
            catch (Java.Lang.Throwable)
            {
                return false;
            }
        }
    }

    public event EventHandler<BarcodeScanEvent>? ScanReceived;

    public void Start()
    {
        if (_registered || !IsAvailable)
        {
            return;
        }

        var filter = new IntentFilter(UrovoIntentScannerProfile.Action);
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            _context.RegisterReceiver(_receiver, filter, ReceiverFlags.Exported);
        }
        else
        {
            _context.RegisterReceiver(_receiver, filter);
        }

        _registered = true;
    }

    public void Stop()
    {
        if (!_registered)
        {
            return;
        }

        _context.UnregisterReceiver(_receiver);
        _registered = false;
    }

    private void Accept(Intent intent)
    {
        var value = intent.GetStringExtra(UrovoIntentScannerProfile.BarcodeStringExtra);
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        var symbology = intent.HasExtra(UrovoIntentScannerProfile.BarcodeTypeExtra)
            ? intent.GetByteExtra(UrovoIntentScannerProfile.BarcodeTypeExtra, 0)
                .ToString(CultureInfo.InvariantCulture)
            : null;

        ScanReceived?.Invoke(
            this,
            new BarcodeScanEvent(
                value,
                symbology,
                DateTimeOffset.UtcNow,
                Source,
                TechnicalDetails));
    }

    private sealed class ScannerBroadcastReceiver(AndroidIntentBarcodeScanner scanner)
        : BroadcastReceiver
    {
        public override void OnReceive(Context? context, Intent? intent)
        {
            if (intent?.Action == UrovoIntentScannerProfile.Action)
            {
                scanner.Accept(intent);
            }
        }
    }
}

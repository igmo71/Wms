using BarcodeScanning;
using Wms.Mobile.Scanning;

namespace Wms.Mobile;

public partial class OperationalCameraScannerView : ContentView
{
    private IOperationalBarcodeScanner? _scanner;
    private int _detectionAccepted;
    private bool _requested;
    private bool _starting;

    public OperationalCameraScannerView()
    {
        InitializeComponent();
    }

    public void Configure(IOperationalBarcodeScanner scanner) => _scanner = scanner;

    public async Task StartAsync()
    {
        if (_scanner?.ActiveSource != BarcodeScanSource.Camera)
        {
            Stop();
            return;
        }

        _requested = true;
        IsVisible = true;
        Interlocked.Exchange(ref _detectionAccepted, 0);
        CameraInstructionLabel.Text = "Совместите нужный код с красной точкой.";

        if (_starting)
        {
            return;
        }

        _starting = true;
        try
        {
            var permission = await Permissions.CheckStatusAsync<Permissions.Camera>();
            if (permission != PermissionStatus.Granted)
            {
                permission = await Permissions.RequestAsync<Permissions.Camera>();
            }

            if (!_requested || _scanner?.ActiveSource != BarcodeScanSource.Camera)
            {
                return;
            }

            if (permission != PermissionStatus.Granted)
            {
                CameraInstructionLabel.Text =
                    "Нет доступа к камере. Разрешите его в настройках устройства.";
                return;
            }

            CameraView.CameraEnabled = true;
        }
        finally
        {
            _starting = false;
        }
    }

    public void Stop()
    {
        _requested = false;
        CameraView.CameraEnabled = false;
        IsVisible = false;
    }

    private void OnDetectionFinished(object? sender, OnDetectionFinishedEventArg e)
    {
        var value = e.BarcodeResults.FirstOrDefault()?.DisplayValue;
        if (string.IsNullOrEmpty(value)
            || Interlocked.CompareExchange(ref _detectionAccepted, 1, 0) != 0)
        {
            return;
        }

        MainThread.BeginInvokeOnMainThread(() => AcceptDetection(value));
    }

    private void AcceptDetection(string value)
    {
        if (!_requested)
        {
            Interlocked.Exchange(ref _detectionAccepted, 0);
            return;
        }

        CameraView.CameraEnabled = false;
        if (_scanner?.TryAcceptCameraScan(value, symbology: null) != true)
        {
            Interlocked.Exchange(ref _detectionAccepted, 0);
            CameraView.CameraEnabled = true;
        }
    }
}

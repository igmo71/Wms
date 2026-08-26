using BarcodeScanning;
using Wms.Mobile.Scanning;

namespace Wms.Mobile;

public partial class CameraScannerPage : ContentPage
{
    private readonly ICameraBarcodeScanner _cameraScanner;
    private int _completionStarted;

    public CameraScannerPage(ICameraBarcodeScanner cameraScanner)
    {
        InitializeComponent();
        _cameraScanner = cameraScanner;
    }

    public event EventHandler<BarcodeScanEvent>? ScanCompleted;

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _cameraScanner.ScanReceived += OnScanReceived;

        var permission = await Permissions.CheckStatusAsync<Permissions.Camera>();
        if (permission != PermissionStatus.Granted)
        {
            permission = await Permissions.RequestAsync<Permissions.Camera>();
        }

        if (permission != PermissionStatus.Granted)
        {
            InstructionLabel.Text =
                "Доступ к камере не предоставлен. Нажмите «Отмена», чтобы вернуться.";
            return;
        }

        CameraView.CameraEnabled = true;
    }

    protected override void OnDisappearing()
    {
        CameraView.CameraEnabled = false;
        _cameraScanner.ScanReceived -= OnScanReceived;
        base.OnDisappearing();
    }

    private void OnDetectionFinished(object? sender, OnDetectionFinishedEventArg e)
    {
        var result = e.BarcodeResults.FirstOrDefault();
        if (string.IsNullOrEmpty(result?.DisplayValue)
            || Interlocked.CompareExchange(ref _completionStarted, 1, 0) != 0)
        {
            return;
        }

        _cameraScanner.TryAccept(result.DisplayValue, symbology: null);
        MainThread.BeginInvokeOnMainThread(() => _ = CloseAsync());
    }

    private void OnScanReceived(object? sender, BarcodeScanEvent scanEvent)
    {
        ScanCompleted?.Invoke(this, scanEvent);
    }

    private async void OnCancelClicked(object? sender, EventArgs e)
    {
        await CloseAsync();
    }

    private async Task CloseAsync()
    {
        CameraView.CameraEnabled = false;
        await Navigation.PopModalAsync();
    }
}

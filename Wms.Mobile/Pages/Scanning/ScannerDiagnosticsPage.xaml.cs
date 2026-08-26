using Wms.Mobile.Scanning;
using Wms.Mobile.Services;

namespace Wms.Mobile;

public partial class ScannerDiagnosticsPage : ContentPage
{
    private readonly MobileApiClient _apiClient;
    private readonly ILifecycleBarcodeScanner _intentScanner;
    private readonly ICameraBarcodeScanner _cameraScanner;
    private readonly Queue<BarcodeScanEvent> _recentScans = new();
    private bool _scannerSubscribed;

    public ScannerDiagnosticsPage(
        MobileApiClient apiClient,
        ILifecycleBarcodeScanner intentScanner,
        ICameraBarcodeScanner cameraScanner)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _intentScanner = intentScanner;
        _cameraScanner = cameraScanner;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (_scannerSubscribed)
        {
            return;
        }

        _intentScanner.ScanReceived += OnScanReceived;
        _scannerSubscribed = true;
    }

    protected override void OnDisappearing()
    {
        if (_scannerSubscribed)
        {
            _intentScanner.ScanReceived -= OnScanReceived;
            _scannerSubscribed = false;
        }

        base.OnDisappearing();
    }

    private async void OnCameraClicked(object? sender, EventArgs e)
    {
        if (!_cameraScanner.IsAvailable)
        {
            ScannerStatusLabel.Text = "На устройстве не обнаружена камера.";
            return;
        }

        var cameraPage = new CameraScannerPage(_cameraScanner);
        cameraPage.ScanCompleted += OnScanReceived;
        await Navigation.PushModalAsync(cameraPage);
    }

    private void OnScanReceived(object? sender, BarcodeScanEvent scanEvent)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            _recentScans.Enqueue(scanEvent);
            while (_recentScans.Count > 5)
            {
                _recentScans.Dequeue();
            }

            ScannerStatusLabel.Text =
                $"Получено: {scanEvent.ReceivedAt.ToLocalTime():HH:mm:ss} · {GetSourceName(scanEvent.Source)}";
            ScanHistoryLabel.Text = string.Join(
                Environment.NewLine,
                _recentScans.Reverse().Select(scan =>
                    $"{scan.ReceivedAt.ToLocalTime():HH:mm:ss}  [{scan.Value}]"));

            await ResolveScanAsync(scanEvent.Value);
        });
    }

    private async Task ResolveScanAsync(string barcode)
    {
        ResolvedBarcodeLabel.Text = "Поиск в WMS…";

        try
        {
            if (ScanContextPicker.SelectedIndex == 1)
            {
                var sku = await _apiClient.ResolveSkuAsync(barcode);
                ResolvedBarcodeLabel.Text = $"Товар: {sku.Name}\nКод: {sku.Code}";
            }
            else
            {
                var location = await _apiClient.ResolveStorageLocationAsync(barcode);
                ResolvedBarcodeLabel.Text =
                    $"Ячейка: {location.Address} · {location.Name}\nСклад: {location.WarehouseName}";
            }
        }
        catch (MobileApiException exception)
        {
            ResolvedBarcodeLabel.Text = exception.Message;
        }
        catch (HttpRequestException)
        {
            ResolvedBarcodeLabel.Text = "Сервер WMS недоступен.";
        }
    }

    private static string GetSourceName(BarcodeScanSource source) => source switch
    {
        BarcodeScanSource.EmbeddedScanner => "встроенный сканер",
        BarcodeScanSource.Camera => "камера",
        BarcodeScanSource.KeyboardWedge => "keyboard wedge",
        _ => source.ToString()
    };
}

using Wms.Contracts.Mobile.V1;
using Wms.Mobile.Scanning;
using Wms.Mobile.Services;

namespace Wms.Mobile;

public partial class DirectInventoryTransferPage : ContentPage
{
    private readonly MobileApiClient _apiClient;
    private readonly ILifecycleBarcodeScanner _intentScanner;
    private readonly ICameraBarcodeScanner _cameraScanner;
    private readonly MobileInventoryTransferSummaryResponse _transfer;
    private MobileStorageLocationResponse? _sourceLocation;
    private MobileSkuResponse? _sku;
    private bool _scannerSubscribed;
    private bool _resolving;

    public DirectInventoryTransferPage(
        MobileApiClient apiClient,
        ILifecycleBarcodeScanner intentScanner,
        ICameraBarcodeScanner cameraScanner,
        MobileInventoryTransferSummaryResponse transfer)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _intentScanner = intentScanner;
        _cameraScanner = cameraScanner;
        _transfer = transfer;

        TransferNumberLabel.Text = $"Перемещение {transfer.Number}";
        TransferContextLabel.Text = $"Склад: {transfer.WarehouseName}\nСтатус: {GetStatusText(transfer.Status)}";
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
            ErrorLabel.Text = "На устройстве не обнаружена камера.";
            return;
        }

        var cameraPage = new CameraScannerPage(_cameraScanner);
        cameraPage.ScanCompleted += OnScanReceived;
        await Navigation.PushModalAsync(cameraPage);
    }

    private void OnScanReceived(object? sender, BarcodeScanEvent scanEvent)
    {
        MainThread.BeginInvokeOnMainThread(async () => await ResolveScanAsync(scanEvent.Value));
    }

    private async Task ResolveScanAsync(string barcode)
    {
        if (_resolving || _sku is not null)
        {
            return;
        }

        _resolving = true;
        SetBusy(true);
        ErrorLabel.Text = string.Empty;

        try
        {
            if (_sourceLocation is null)
            {
                _sourceLocation = await _apiClient.ResolveStorageLocationAsync(
                    barcode,
                    _transfer.WarehouseId,
                    MobileStorageLocationContext.Storage);

                SourceLocationLabel.Text =
                    $"{_sourceLocation.Address} · {_sourceLocation.Name}";
                SourceCard.IsVisible = true;
                StepLabel.Text = "2. Товар";
                InstructionLabel.Text = "Отсканируйте штрихкод товара.";
            }
            else
            {
                _sku = await _apiClient.ResolveSkuAsync(barcode);
                SkuLabel.Text = $"{_sku.Name}\nКод: {_sku.Code}";
                SkuCard.IsVisible = true;
                StepLabel.Text = "Товар выбран";
                InstructionLabel.Text = "Следующий шаг — ввод количества.";
                CameraButton.IsVisible = false;
            }
        }
        catch (MobileApiException exception)
        {
            ErrorLabel.Text = exception.Message;
        }
        catch (HttpRequestException)
        {
            ErrorLabel.Text = "Сервер WMS недоступен.";
        }
        finally
        {
            SetBusy(false);
            _resolving = false;
        }
    }

    private void SetBusy(bool isBusy)
    {
        ProgressIndicator.IsVisible = isBusy;
        ProgressIndicator.IsRunning = isBusy;
        CameraButton.IsEnabled = !isBusy;
    }

    private static string GetStatusText(MobileInventoryTransferStatus status) => status switch
    {
        MobileInventoryTransferStatus.Draft => "Черновик",
        MobileInventoryTransferStatus.InProgress => "В работе",
        MobileInventoryTransferStatus.Completed => "Завершено",
        _ => status.ToString()
    };
}

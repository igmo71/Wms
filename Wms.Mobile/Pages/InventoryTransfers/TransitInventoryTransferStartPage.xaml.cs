using Wms.Contracts.Mobile.V1;
using Wms.Mobile.Scanning;
using Wms.Mobile.Services;

namespace Wms.Mobile;

public partial class TransitInventoryTransferStartPage : ContentPage
{
    private readonly MobileApiClient _apiClient;
    private readonly IOperationalBarcodeScanner _scanner;
    private readonly MobileWarehouseResponse _warehouse;
    private MobileStorageLocationResponse? _transitLocation;
    private MobileInventoryTransferSummaryResponse? _activeTransfer;
    private Guid? _pendingCreateRequestId;
    private bool _scannerSubscribed;
    private bool _busy;

    public TransitInventoryTransferStartPage(
        MobileApiClient apiClient,
        IOperationalBarcodeScanner scanner,
        MobileWarehouseResponse warehouse)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _scanner = scanner;
        _warehouse = warehouse;
        CameraScannerView.Configure(scanner);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (_scannerSubscribed)
        {
            return;
        }

        _scanner.ScanReceived += OnScanReceived;
        _scannerSubscribed = true;
        _ = UpdateCameraAsync();
    }

    protected override void OnDisappearing()
    {
        CameraScannerView.Stop();
        if (_scannerSubscribed)
        {
            _scanner.ScanReceived -= OnScanReceived;
            _scannerSubscribed = false;
        }

        base.OnDisappearing();
    }

    private void OnScanReceived(object? sender, BarcodeScanEvent scanEvent) =>
        MainThread.BeginInvokeOnMainThread(async () => await ResolveTransitLocationAsync(
            scanEvent.Value));

    private async Task UpdateCameraAsync()
    {
        if (_scanner.ActiveSource is null)
        {
            CameraScannerView.Stop();
            ErrorLabel.Text = "На устройстве не найден доступный сканер.";
            return;
        }

        if (_scanner.ActiveSource == BarcodeScanSource.Camera
            && !_busy
            && _transitLocation is null)
        {
            await CameraScannerView.StartAsync();
        }
        else
        {
            CameraScannerView.Stop();
        }
    }

    private async Task ResolveTransitLocationAsync(string barcode)
    {
        if (_busy)
        {
            return;
        }

        SetBusy(true);
        ErrorLabel.Text = string.Empty;
        ContinueButton.IsVisible = false;
        LocationCard.IsVisible = false;

        try
        {
            var location = await _apiClient.ResolveStorageLocationAsync(
                barcode,
                _warehouse.Id,
                MobileStorageLocationContext.Transit);
            var transfer = await _apiClient.GetInventoryTransferByTransitStorageLocationAsync(
                location.Id);

            _transitLocation = location;
            _activeTransfer = transfer;
            _pendingCreateRequestId = null;
            LocationLabel.Text = $"{location.Address} · {location.Name}";
            LocationStateLabel.Text = transfer is null
                ? "Ячейка свободна. Можно создать перемещение."
                : $"Используется перемещением {transfer.Number}.";
            ContinueButton.Text = transfer is null
                ? "Создать перемещение"
                : "Открыть перемещение";
            LocationCard.IsVisible = true;
            ContinueButton.IsVisible = true;
            ContinueButton.Unfocus();
            InstructionLabel.Text = "Проверьте транзитную ячейку.";
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
            await UpdateCameraAsync();
        }
    }

    private async void OnContinueClicked(object? sender, EventArgs e)
    {
        if (_busy || _transitLocation is null)
        {
            return;
        }

        if (_activeTransfer is not null)
        {
            await OpenTransferAsync(_activeTransfer);
            return;
        }

        _pendingCreateRequestId ??= Guid.NewGuid();
        SetBusy(true);
        ErrorLabel.Text = string.Empty;

        try
        {
            var transfer = await _apiClient.CreateInventoryTransferAsync(
                _warehouse.Id,
                _pendingCreateRequestId.Value,
                _transitLocation.Id);
            _pendingCreateRequestId = null;
            await OpenTransferAsync(transfer);
        }
        catch (MobileApiException exception)
        {
            _pendingCreateRequestId = null;
            ErrorLabel.Text = exception.Message;
        }
        catch (HttpRequestException)
        {
            ErrorLabel.Text = "Ответ сервера не получен. Нажмите «Повторить».";
            ContinueButton.Text = "Повторить";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task OpenTransferAsync(MobileInventoryTransferSummaryResponse transfer)
    {
        await Navigation.PushAsync(new InventoryTransferDetailsPage(
            _apiClient,
            _scanner,
            transfer));
        Navigation.RemovePage(this);
    }

    private void SetBusy(bool isBusy)
    {
        _busy = isBusy;
        ProgressIndicator.IsVisible = isBusy;
        ProgressIndicator.IsRunning = isBusy;
        ContinueButton.IsEnabled = !isBusy;
    }

    private void OnContinueButtonLoaded(object? sender, EventArgs e)
    {
#if ANDROID
        if (ContinueButton.Handler?.PlatformView is Android.Widget.Button button)
        {
            button.Focusable = false;
            button.FocusableInTouchMode = false;
        }
#endif
    }
}

using Wms.Contracts.Mobile.V1;
using Wms.Mobile.Scanning;
using Wms.Mobile.Services;

namespace Wms.Mobile;

public partial class ShippingOrderShippingPage : ContentPage
{
    private readonly MobileApiClient _apiClient;
    private readonly IOperationalBarcodeScanner _scanner;
    private MobileShippingOrderDetailsResponse? _details;
    private ShippingPageMode _mode = ShippingPageMode.Ready;
    private MobileStorageLocationResponse? _scannedLocation;
    private string? _scannedLocationBarcode;
    private Guid? _pendingShippingRequestId;
    private bool _isVisible;
    private bool _scannerSubscribed;
    private bool _busy;

    public ShippingOrderShippingPage(
        MobileApiClient apiClient,
        IOperationalBarcodeScanner scanner)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _scanner = scanner;
        CameraScannerView.Configure(scanner);
    }

    public IReadOnlyList<MobileShippingOrderLineResponse> Lines { get; private set; } = [];

    private MobileShippingOrderDetailsResponse Details =>
        _details ?? throw new InvalidOperationException("Расходный ордер не загружен.");

    private bool IsScanExpected => _isVisible
        && !_busy
        && _pendingShippingRequestId is null
        && _mode == ShippingPageMode.LocationScanning;

    public void Show(MobileShippingOrderDetailsResponse details)
    {
        _scannedLocation = null;
        _scannedLocationBarcode = null;
        _pendingShippingRequestId = null;
        ApplyDetails(details);
        SetMode(ShippingPageMode.Ready);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _isVisible = true;
        if (!_scannerSubscribed)
        {
            _scanner.ScanReceived += OnScanReceived;
            _scannerSubscribed = true;
        }

        await UpdateCameraAsync();
    }

    protected override void OnDisappearing()
    {
        _isVisible = false;
        CameraScannerView.Stop();
        if (_scannerSubscribed)
        {
            _scanner.ScanReceived -= OnScanReceived;
            _scannerSubscribed = false;
        }

        base.OnDisappearing();
    }

    protected override bool OnBackButtonPressed()
    {
        if (_busy || _pendingShippingRequestId is not null)
        {
            ErrorLabel.Text = _pendingShippingRequestId is not null
                ? "Сначала повторите отгрузку с той же позицией."
                : "Дождитесь завершения операции.";
            return true;
        }

        return base.OnBackButtonPressed();
    }

    private async void OnStartShippingClicked(object? sender, EventArgs e)
    {
        if (_busy
            || _pendingShippingRequestId is not null
            || Details.Order.Status != MobileShippingOrderStatus.ReadyForShipment)
        {
            return;
        }

        ErrorLabel.Text = string.Empty;
        SetMode(ShippingPageMode.LocationScanning);
        await UpdateCameraAsync();
    }

    private void OnScanReceived(object? sender, BarcodeScanEvent scanEvent) =>
        MainThread.BeginInvokeOnMainThread(async () => await ResolveShippingLocationAsync(scanEvent.Value));

    private async Task ResolveShippingLocationAsync(string barcode)
    {
        if (!IsScanExpected)
        {
            return;
        }

        SetBusy(true);
        ErrorLabel.Text = string.Empty;
        try
        {
            var location = await _apiClient.ResolveStorageLocationAsync(
                barcode,
                Details.Order.WarehouseId,
                MobileStorageLocationContext.Shipping);
            if (Details.Order.ShippingLocation is not { } expectedLocation
                || location.Id != expectedLocation.Id)
            {
                ErrorLabel.Text = "Отсканирована не та позиция отгрузки, которая указана в ордере.";
                return;
            }

            _scannedLocation = location;
            _scannedLocationBarcode = barcode;
            ScannedLocationLabel.Text = $"Позиция подтверждена: {location.Address} · {location.Name}";
            SetMode(ShippingPageMode.Confirmation);
        }
        catch (MobileApiException exception)
        {
            ErrorLabel.Text = exception.Message;
        }
        catch (HttpRequestException)
        {
            ErrorLabel.Text = "Сервер WMS недоступен. Повторите сканирование позиции.";
        }
        finally
        {
            SetBusy(false);
            await UpdateCameraAsync();
        }
    }

    private async void OnCancelShippingClicked(object? sender, EventArgs e)
    {
        if (_busy)
        {
            return;
        }

        if (_pendingShippingRequestId is not null)
        {
            ErrorLabel.Text = "Сначала повторите отгрузку с той же позицией.";
            return;
        }

        _scannedLocation = null;
        _scannedLocationBarcode = null;
        ConfirmShippingButton.Text = "Отгрузить";
        ErrorLabel.Text = string.Empty;
        SetMode(ShippingPageMode.LocationScanning);
        await UpdateCameraAsync();
    }

    private async void OnConfirmShippingClicked(object? sender, EventArgs e)
    {
        if (_busy || _scannedLocation is null || _scannedLocationBarcode is null)
        {
            return;
        }

        _pendingShippingRequestId ??= Guid.NewGuid();
        SetBusy(true);
        ErrorLabel.Text = string.Empty;
        try
        {
            var response = await _apiClient.ShipShippingOrderAsync(
                Details.Order.Id,
                _scannedLocationBarcode,
                _pendingShippingRequestId.Value);
            _pendingShippingRequestId = null;
            ConfirmShippingButton.Text = "Отгрузить";
            ApplyDetails(response.Details);
            if (_isVisible)
            {
                await DisplayAlertAsync("Готово", "Ордер отгружен.", "ОК");
                if (_isVisible)
                {
                    await Navigation.PopAsync();
                }
            }
        }
        catch (MobileApiException exception)
        {
            _pendingShippingRequestId = null;
            ConfirmShippingButton.Text = "Отгрузить";
            ErrorLabel.Text = exception.Message;
        }
        catch (HttpRequestException)
        {
            ConfirmShippingButton.Text = "Повторить отгрузку";
            ErrorLabel.Text = "Ответ сервера не получен. Повторите отгрузку с той же позицией.";
        }
        finally
        {
            SetBusy(false);
            await UpdateCameraAsync();
        }
    }

    private void ApplyDetails(MobileShippingOrderDetailsResponse details)
    {
        _details = details;
        Lines = details.Lines;
        OnPropertyChanged(nameof(Lines));
        NumberLabel.Text = $"Ордер {details.Order.Number}";
        WarehouseLabel.Text = $"Склад: {details.Order.WarehouseName}";
        ReceiverLabel.Text = $"Получатель: {details.Order.ReceiverName}";
        LocationLabel.Text = details.Order.ShippingLocation is null
            ? "Позиция отгрузки не указана"
            : $"Позиция отгрузки: {details.Order.ShippingLocation.Address}";
        ProgressLabel.Text = $"К отгрузке: {details.Order.Progress.FactQuantity:g}";
        ShippingSummaryLabel.Text = $"Строк: {details.Lines.Count}; "
            + $"фактическое количество: {details.Order.Progress.FactQuantity:g}.";
    }

    private void SetMode(ShippingPageMode mode)
    {
        _mode = mode;
        ConfirmationPanel.IsVisible = mode == ShippingPageMode.Confirmation;
        (StepLabel.Text, InstructionLabel.Text) = mode switch
        {
            ShippingPageMode.LocationScanning => (
                "Позиция отгрузки",
                "Повторно отсканируйте позицию отгрузки, указанную в ордере."),
            ShippingPageMode.Confirmation => (
                "Подтверждение отгрузки",
                "Проверьте позицию и итоговое количество."),
            _ => (
                "Финальная отгрузка",
                "Проверьте итог ордера и нажмите «Отгрузить».")
        };
        RefreshActionAvailability();
    }

    private async Task UpdateCameraAsync()
    {
        if (_scanner.ActiveSource == BarcodeScanSource.Camera && IsScanExpected)
        {
            await CameraScannerView.StartAsync();
        }
        else
        {
            CameraScannerView.Stop();
        }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        ProgressIndicator.Opacity = busy ? 1 : 0;
        RefreshActionAvailability();
    }

    private void RefreshActionAvailability()
    {
        StartShippingButton.IsVisible = _mode == ShippingPageMode.Ready;
        StartShippingButton.IsEnabled = !_busy && _pendingShippingRequestId is null;
        ConfirmShippingButton.IsEnabled = !_busy && _scannedLocationBarcode is not null;
        CancelShippingButton.IsEnabled = !_busy && _pendingShippingRequestId is null;
    }

    private void OnNonScanControlLoaded(object? sender, EventArgs e)
    {
        if (sender is VisualElement element)
        {
            DisableAndroidFocus(element);
        }
    }

    private static void DisableAndroidFocus(VisualElement element)
    {
#if ANDROID
        if (element.Handler?.PlatformView is Android.Views.View view)
        {
            view.Focusable = false;
            view.FocusableInTouchMode = false;
        }
#endif
    }

    private enum ShippingPageMode
    {
        Ready,
        LocationScanning,
        Confirmation
    }
}

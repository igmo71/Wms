using Wms.Contracts.Mobile.V1;
using Wms.Mobile.Scanning;
using Wms.Mobile.Services;

namespace Wms.Mobile;

public partial class ShippingOrderPickingPage : ContentPage
{
    private readonly MobileApiClient _apiClient;
    private readonly IOperationalBarcodeScanner _scanner;
    private MobileShippingOrderDetailsResponse? _details;
    private PickingPageMode _mode = PickingPageMode.Ready;
    private MobileStorageLocationResponse? _scannedLocation;
    private string? _scannedLocationBarcode;
    private Guid? _pendingStartRequestId;
    private bool _isVisible;
    private bool _scannerSubscribed;
    private bool _busy;

    public ShippingOrderPickingPage(
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

    public void Show(MobileShippingOrderDetailsResponse details)
    {
        _scannedLocation = null;
        _scannedLocationBarcode = null;
        _pendingStartRequestId = null;
        ApplyDetails(details);
        SetMode(details.Order.Status == MobileShippingOrderStatus.Prepared
            ? PickingPageMode.Ready
            : PickingPageMode.InProgress);
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

    private async void OnStartPickingClicked(object? sender, EventArgs e)
    {
        if (_busy
            || _pendingStartRequestId is not null
            || Details.Order.Status != MobileShippingOrderStatus.Prepared)
        {
            return;
        }

        ErrorLabel.Text = string.Empty;
        SetMode(PickingPageMode.LocationScanning);
        await UpdateCameraAsync();
    }

    private void OnScanReceived(object? sender, BarcodeScanEvent scanEvent) =>
        MainThread.BeginInvokeOnMainThread(async () => await HandleScanAsync(scanEvent.Value));

    private async Task HandleScanAsync(string barcode)
    {
        if (_busy || _mode != PickingPageMode.LocationScanning)
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
            _scannedLocation = location;
            _scannedLocationBarcode = barcode;
            ScannedLocationLabel.Text = $"{location.Address} · {location.Name}";
            SetMode(PickingPageMode.LocationConfirmation);
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

    private async void OnConfirmLocationClicked(object? sender, EventArgs e)
    {
        if (_busy || _scannedLocation is null || _scannedLocationBarcode is null)
        {
            return;
        }

        _pendingStartRequestId ??= Guid.NewGuid();
        SetBusy(true);
        ErrorLabel.Text = string.Empty;
        try
        {
            var response = await _apiClient.StartShippingOrderPickingAsync(
                Details.Order.Id,
                _scannedLocationBarcode,
                _pendingStartRequestId.Value);
            _pendingStartRequestId = null;
            _scannedLocation = null;
            _scannedLocationBarcode = null;
            ConfirmLocationButton.Text = "Подтвердить";
            ApplyDetails(response.Details);
            SetMode(PickingPageMode.InProgress);
        }
        catch (MobileApiException exception)
        {
            _pendingStartRequestId = null;
            ConfirmLocationButton.Text = "Подтвердить";
            ErrorLabel.Text = exception.Message;
        }
        catch (HttpRequestException)
        {
            ConfirmLocationButton.Text = "Повторить начало";
            ErrorLabel.Text = "Ответ сервера не получен. Повторите начало с той же позицией.";
        }
        finally
        {
            SetBusy(false);
            await UpdateCameraAsync();
        }
    }

    private async void OnCancelLocationClicked(object? sender, EventArgs e)
    {
        if (_busy)
        {
            return;
        }

        if (_pendingStartRequestId is not null)
        {
            ErrorLabel.Text = "Сначала повторите начало отбора с той же позицией.";
            return;
        }

        _scannedLocation = null;
        _scannedLocationBarcode = null;
        ConfirmLocationButton.Text = "Подтвердить";
        SetMode(PickingPageMode.LocationScanning);
        await UpdateCameraAsync();
    }

    private void ApplyDetails(MobileShippingOrderDetailsResponse details)
    {
        _details = details;
        Lines = details.Lines;
        OnPropertyChanged(nameof(Lines));

        NumberLabel.Text = $"Ордер {details.Order.Number}";
        StatusLabel.Text = MapStatus(details.Order.Status);
        WarehouseLabel.Text = $"Склад: {details.Order.WarehouseName}";
        ReceiverLabel.Text = $"Получатель: {details.Order.ReceiverName}";
        QueueLabel.Text = $"Очередь: {details.Order.Queue}";
        PlannedDateLabel.Text = details.Order.PlannedShippingDate is DateTime plannedDate
            ? $"План отгрузки: {plannedDate:dd.MM.yyyy HH:mm}"
            : "План отгрузки не указан";
        LocationLabel.Text = details.Order.ShippingLocation is null
            ? "Позиция отгрузки не указана"
            : $"Позиция отгрузки: {details.Order.ShippingLocation.Address}";
        ProgressLabel.Text = $"Отобрано: {details.Order.Progress.FactQuantity:g} "
            + $"из {details.Order.Progress.PlanQuantity:g}";
        CommentLabel.Text = details.Order.Comment;
        CommentLabel.IsVisible = !string.IsNullOrWhiteSpace(details.Order.Comment);
        RefreshActionAvailability();
    }

    private void SetMode(PickingPageMode mode)
    {
        _mode = mode;
        LocationConfirmationPanel.IsVisible = mode == PickingPageMode.LocationConfirmation;
        (StepLabel.Text, InstructionLabel.Text) = mode switch
        {
            PickingPageMode.Ready => (
                "Начало отбора",
                "Проверьте ордер и нажмите «Начать отбор»."),
            PickingPageMode.LocationScanning => (
                "Позиция отгрузки",
                "Отсканируйте активную позицию зоны отгрузки этого склада."),
            PickingPageMode.LocationConfirmation => (
                "Подтверждение позиции",
                "Проверьте адрес и подтвердите начало отбора."),
            _ => (
                "Отбор начат",
                "Позиция отгрузки закреплена за ордером.")
        };
        RefreshActionAvailability();
    }

    private async Task UpdateCameraAsync()
    {
        if (_isVisible
            && _scanner.ActiveSource == BarcodeScanSource.Camera
            && !_busy
            && _mode == PickingPageMode.LocationScanning)
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
        if (_details is null)
        {
            return;
        }

        StartPickingButton.IsVisible = _mode == PickingPageMode.Ready;
        StartPickingButton.IsEnabled = !_busy && _pendingStartRequestId is null;
        ConfirmLocationButton.IsEnabled = !_busy && _scannedLocationBarcode is not null;
        CancelLocationButton.IsEnabled = !_busy && _pendingStartRequestId is null;
    }

    private static string MapStatus(MobileShippingOrderStatus status) => status switch
    {
        MobileShippingOrderStatus.Prepared => "Подготовлен",
        MobileShippingOrderStatus.ReadyForPicking => "В отборе",
        MobileShippingOrderStatus.ReadyForVerification => "Готов к проверке",
        MobileShippingOrderStatus.InVerification => "На проверке",
        MobileShippingOrderStatus.Verified => "Проверен",
        _ => "Отбор"
    };

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

    private enum PickingPageMode
    {
        Ready,
        LocationScanning,
        LocationConfirmation,
        InProgress
    }
}

using System.Globalization;
using Wms.Contracts.Mobile.V1;
using Wms.Mobile.Scanning;
using Wms.Mobile.Services;

namespace Wms.Mobile;

public partial class ReceivingOrderPutawayMovementPage : ContentPage
{
    private readonly MobileApiClient _apiClient;
    private readonly IOperationalBarcodeScanner _scanner;
    private MobileReceivingOrderDetailsResponse? _details;
    private MobileReceivingOrderLineResponse? _selectedLine;
    private MobileStorageLocationResponse? _destination;
    private string? _destinationBarcode;
    private Action<MobileReceivingOrderCommandResponse>? _completed;
    private MovementPageMode _mode;
    private bool _isVisible;
    private bool _scannerSubscribed;
    private bool _busy;
    private double _quantity;
    private Guid? _pendingRequestId;

    public ReceivingOrderPutawayMovementPage(
        MobileApiClient apiClient,
        IOperationalBarcodeScanner scanner)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _scanner = scanner;
        CameraScannerView.Configure(scanner);
    }

    public IReadOnlyList<MobileReceivingOrderLineCandidateResponse> ScanCandidates { get; private set; } = [];

    private MobileReceivingOrderDetailsResponse Details =>
        _details ?? throw new InvalidOperationException("Приходный ордер не загружен.");

    private bool IsScanExpected => !_busy
        && _pendingRequestId is null
        && _mode is MovementPageMode.SkuScanning or MovementPageMode.DestinationScanning;

    public void Show(
        MobileReceivingOrderDetailsResponse details,
        MobileReceivingOrderLineResponse? selectedLine,
        Action<MobileReceivingOrderCommandResponse> completed)
    {
        _details = details;
        _selectedLine = selectedLine;
        _completed = completed;
        NumberLabel.Text = $"Ордер {details.Order.Number}";
        ApplySelectedLine();
        SetMode(selectedLine is null
            ? MovementPageMode.SkuScanning
            : MovementPageMode.DestinationScanning);
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
        QuantityEntry.Unfocus();
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
        if (_busy || _pendingRequestId is not null)
        {
            ErrorLabel.Text = _pendingRequestId is not null
                ? "Сначала повторите подтверждение этой же партии."
                : "Дождитесь завершения операции.";
            return true;
        }

        return base.OnBackButtonPressed();
    }

    private void OnScanReceived(object? sender, BarcodeScanEvent scanEvent) =>
        MainThread.BeginInvokeOnMainThread(async () => await HandleScanAsync(scanEvent.Value));

    private async Task HandleScanAsync(string barcode)
    {
        if (!IsScanExpected)
        {
            return;
        }

        if (_mode == MovementPageMode.SkuScanning)
        {
            await ResolveSkuAsync(barcode);
        }
        else
        {
            await ResolveDestinationAsync(barcode);
        }
    }

    private async Task ResolveSkuAsync(string barcode)
    {
        SetBusy(true);
        ErrorLabel.Text = string.Empty;
        try
        {
            var candidates = await _apiClient.ResolveReceivingOrderSkuAsync(
                Details.Order.Id,
                barcode);
            if (candidates.Count == 1)
            {
                SelectLine(candidates[0].LineNumber);
                SetMode(MovementPageMode.DestinationScanning);
                return;
            }

            ScanCandidates = candidates;
            OnPropertyChanged(nameof(ScanCandidates));
            SetMode(MovementPageMode.CandidateSelection);
        }
        catch (MobileApiException exception)
        {
            ErrorLabel.Text = exception.Message;
        }
        catch (HttpRequestException)
        {
            ErrorLabel.Text = "Сервер WMS недоступен. Повторите сканирование товара.";
        }
        finally
        {
            SetBusy(false);
            await UpdateCameraAsync();
        }
    }

    private async void OnCandidateTapped(object? sender, TappedEventArgs e)
    {
        if (_busy || e.Parameter is not MobileReceivingOrderLineCandidateResponse candidate)
        {
            return;
        }

        SelectLine(candidate.LineNumber);
        ScanCandidates = [];
        OnPropertyChanged(nameof(ScanCandidates));
        SetMode(MovementPageMode.DestinationScanning);
        await UpdateCameraAsync();
    }

    private async void OnCancelCandidatesClicked(object? sender, EventArgs e)
    {
        if (_busy)
        {
            return;
        }

        ScanCandidates = [];
        OnPropertyChanged(nameof(ScanCandidates));
        SetMode(MovementPageMode.SkuScanning);
        await UpdateCameraAsync();
    }

    private void SelectLine(int lineNumber)
    {
        _selectedLine = Details.Lines.Single(x => x.LineNumber == lineNumber);
        ApplySelectedLine();
    }

    private void ApplySelectedLine()
    {
        if (_selectedLine is null)
        {
            SelectedSkuLabel.Text = "Товар не выбран";
            SelectedLineLabel.Text = string.Empty;
            return;
        }

        SelectedSkuLabel.Text = _selectedLine.SkuName;
        SelectedLineLabel.Text = $"Строка {_selectedLine.LineNumber} · "
            + $"Осталось {_selectedLine.RemainingPutawayQuantity:0.###} "
            + _selectedLine.UnitOfMeasure;
    }

    private async Task ResolveDestinationAsync(string barcode)
    {
        SetBusy(true);
        ErrorLabel.Text = string.Empty;
        try
        {
            var destination = await _apiClient.ResolveStorageLocationAsync(
                barcode,
                Details.Order.WarehouseId,
                MobileStorageLocationContext.Storage);
            _destination = destination;
            _destinationBarcode = barcode;
            SelectedDestinationLabel.Text = $"Назначение: {destination.Address} · {destination.Name}";
            QuantityEntry.Text = _selectedLine!.RemainingPutawayQuantity?.ToString(
                "0.###",
                CultureInfo.InvariantCulture);
            SetMode(MovementPageMode.Quantity);
            Dispatcher.Dispatch(() => QuantityEntry.Focus());
        }
        catch (MobileApiException exception)
        {
            ErrorLabel.Text = exception.Message;
        }
        catch (HttpRequestException)
        {
            ErrorLabel.Text = "Сервер WMS недоступен. Повторите сканирование адреса.";
        }
        finally
        {
            SetBusy(false);
            await UpdateCameraAsync();
        }
    }

    private async void OnQuantityNextClicked(object? sender, EventArgs e)
    {
        if (_busy || _selectedLine is null || _destination is null)
        {
            return;
        }

        if (!TryReadQuantity(out _quantity))
        {
            return;
        }

        QuantityEntry.Unfocus();
        await QuantityEntry.HideSoftInputAsync(CancellationToken.None);
        ConfirmationLabel.Text = $"{_selectedLine.SkuName}\n"
            + $"Строка: {_selectedLine.LineNumber}\n"
            + $"Адрес: {_destination.Address}\n"
            + $"Количество: {_quantity:0.###} {_selectedLine.UnitOfMeasure}";
        SetMode(MovementPageMode.Confirmation);
    }

    private bool TryReadQuantity(out double quantity)
    {
        var value = (QuantityEntry.Text ?? string.Empty).Trim().Replace(',', '.');
        var remaining = _selectedLine?.RemainingPutawayQuantity ?? 0;
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out quantity)
            && double.IsFinite(quantity)
            && quantity > 0
            && quantity <= remaining)
        {
            return true;
        }

        ErrorLabel.Text = $"Количество должно быть больше нуля и не превышать остаток {remaining:0.###}.";
        return false;
    }

    private async void OnRescanDestinationClicked(object? sender, EventArgs e)
    {
        if (_busy)
        {
            return;
        }

        QuantityEntry.Unfocus();
        await QuantityEntry.HideSoftInputAsync(CancellationToken.None);
        _destination = null;
        _destinationBarcode = null;
        SelectedDestinationLabel.Text = string.Empty;
        SetMode(MovementPageMode.DestinationScanning);
        await UpdateCameraAsync();
    }

    private void OnBackToQuantityClicked(object? sender, EventArgs e)
    {
        if (_busy || _pendingRequestId is not null)
        {
            return;
        }

        SetMode(MovementPageMode.Quantity);
        Dispatcher.Dispatch(() => QuantityEntry.Focus());
    }

    private async void OnConfirmMovementClicked(object? sender, EventArgs e)
    {
        if (_busy
            || _selectedLine is null
            || _destinationBarcode is null
            || _quantity <= 0)
        {
            return;
        }

        _pendingRequestId ??= Guid.NewGuid();
        SetBusy(true);
        ErrorLabel.Text = string.Empty;
        try
        {
            var response = await _apiClient.AddReceivingOrderPutawayMovementAsync(
                Details.Order.Id,
                _selectedLine.LineNumber,
                _destinationBarcode,
                _quantity,
                _pendingRequestId.Value);
            _pendingRequestId = null;
            _completed?.Invoke(response);
            if (_isVisible)
            {
                await Navigation.PopAsync();
            }
        }
        catch (MobileApiException exception)
        {
            _pendingRequestId = null;
            ConfirmMovementButton.Text = "Разместить";
            ErrorLabel.Text = exception.Message;
        }
        catch (HttpRequestException)
        {
            ConfirmMovementButton.Text = "Повторить";
            ErrorLabel.Text = "Ответ сервера не получен. Повторите подтверждение этой же партии.";
        }
        finally
        {
            SetBusy(false);
            await UpdateCameraAsync();
        }
    }

    private async void OnCancelFlowClicked(object? sender, EventArgs e)
    {
        if (_busy)
        {
            return;
        }

        if (_pendingRequestId is not null)
        {
            ErrorLabel.Text = "Сначала повторите подтверждение этой же партии.";
            return;
        }

        await Navigation.PopAsync();
    }

    private void SetMode(MovementPageMode mode)
    {
        _mode = mode;
        CandidatePanel.IsVisible = mode == MovementPageMode.CandidateSelection;
        QuantityPanel.IsVisible = mode == MovementPageMode.Quantity;
        ConfirmationPanel.IsVisible = mode == MovementPageMode.Confirmation;
        (StepLabel.Text, InstructionLabel.Text) = mode switch
        {
            MovementPageMode.SkuScanning => (
                "Товар",
                "Отсканируйте товар с остатком к размещению."),
            MovementPageMode.CandidateSelection => (
                "Строка",
                "Товар найден в нескольких строках. Выберите нужную."),
            MovementPageMode.DestinationScanning => (
                "Позиция хранения",
                "Отсканируйте позицию назначения в зоне хранения."),
            MovementPageMode.Quantity => (
                "Количество",
                "Указан весь остаток строки. Уменьшите его для разделения партии."),
            _ => (
                "Подтверждение",
                "Проверьте товар, адрес и количество.")
        };
        RefreshActionAvailability();
    }

    private async Task UpdateCameraAsync()
    {
        if (_isVisible
            && _scanner.ActiveSource == BarcodeScanSource.Camera
            && IsScanExpected)
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
        ConfirmMovementButton.IsEnabled = !_busy;
        BackToQuantityButton.IsEnabled = !_busy && _pendingRequestId is null;
        CancelFlowButton.IsEnabled = !_busy && _pendingRequestId is null;
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

    private enum MovementPageMode
    {
        SkuScanning,
        CandidateSelection,
        DestinationScanning,
        Quantity,
        Confirmation
    }
}

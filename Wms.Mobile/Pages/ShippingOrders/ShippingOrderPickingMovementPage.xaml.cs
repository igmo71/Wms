using System.Globalization;
using Wms.Contracts.Mobile.V1;
using Wms.Mobile.Scanning;
using Wms.Mobile.Services;

namespace Wms.Mobile;

public partial class ShippingOrderPickingMovementPage : ContentPage
{
    private readonly MobileApiClient _apiClient;
    private readonly IOperationalBarcodeScanner _scanner;
    private MobileShippingOrderDetailsResponse? _details;
    private MobileShippingOrderLineResponse? _selectedLine;
    private MobileStorageLocationResponse? _source;
    private MobileShippingOrderSourceAvailabilityResponse? _sourceAvailability;
    private string? _sourceBarcode;
    private Action<MobileShippingOrderCommandResponse>? _completed;
    private MovementPageMode _mode;
    private bool _isVisible;
    private bool _scannerSubscribed;
    private bool _busy;
    private double _quantity;
    private Guid? _pendingRequestId;

    public ShippingOrderPickingMovementPage(
        MobileApiClient apiClient,
        IOperationalBarcodeScanner scanner)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _scanner = scanner;
        CameraScannerView.Configure(scanner);
    }

    public IReadOnlyList<MobileShippingOrderLineCandidateResponse> ScanCandidates { get; private set; } = [];
    public IReadOnlyList<MobileShippingOrderSourceAvailabilityResponse> SourceHints { get; private set; } = [];

    private MobileShippingOrderDetailsResponse Details =>
        _details ?? throw new InvalidOperationException("Расходный ордер не загружен.");

    private bool IsScanExpected => !_busy
        && _pendingRequestId is null
        && _mode is MovementPageMode.SkuScanning or MovementPageMode.SourceScanning;

    public void Show(
        MobileShippingOrderDetailsResponse details,
        MobileShippingOrderLineResponse? selectedLine,
        Action<MobileShippingOrderCommandResponse> completed)
    {
        _details = details;
        _selectedLine = selectedLine;
        _completed = completed;
        _source = null;
        _sourceAvailability = null;
        _sourceBarcode = null;
        _quantity = 0;
        _pendingRequestId = null;
        ConfirmMovementButton.Text = "Отобрать";
        QuantityEntry.Text = string.Empty;
        ErrorLabel.Text = string.Empty;
        NumberLabel.Text = $"Ордер {details.Order.Number}";
        ApplySelectedLine();
        SetMode(selectedLine is null
            ? MovementPageMode.SkuScanning
            : MovementPageMode.SourceScanning);
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

        if (_selectedLine is not null)
        {
            await LoadSourceHintsAsync();
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
                ? "Сначала повторите подтверждение этого же движения."
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
            await ResolveSourceAsync(barcode);
        }
    }

    private async Task ResolveSkuAsync(string barcode)
    {
        SetBusy(true);
        ErrorLabel.Text = string.Empty;
        try
        {
            var candidates = await _apiClient.ResolveShippingOrderSkuAsync(
                Details.Order.Id,
                barcode);
            if (candidates.Count == 1)
            {
                SelectLine(candidates[0].LineNumber);
                SetMode(MovementPageMode.SourceScanning);
                await LoadSourceHintsAsync();
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
        if (_busy || e.Parameter is not MobileShippingOrderLineCandidateResponse candidate)
        {
            return;
        }

        SelectLine(candidate.LineNumber);
        ScanCandidates = [];
        OnPropertyChanged(nameof(ScanCandidates));
        SetMode(MovementPageMode.SourceScanning);
        await LoadSourceHintsAsync();
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
            + $"Осталось {_selectedLine.RemainingQuantity:0.###} "
            + _selectedLine.UnitOfMeasure;
    }

    private async Task LoadSourceHintsAsync()
    {
        if (_selectedLine is null)
        {
            return;
        }

        SourceHints = [];
        OnPropertyChanged(nameof(SourceHints));
        try
        {
            SourceHintsStatusLabel.Text = "Обновление доступности...";
            var sources = await _apiClient.GetShippingOrderSourcesAsync(
                Details.Order.Id,
                _selectedLine.LineNumber);
            SourceHints = sources;
            OnPropertyChanged(nameof(SourceHints));
            SourceHintsStatusLabel.Text = sources.Count == 0
                ? "Доступные позиции отсутствуют."
                : $"Позиций: {sources.Count}. Источник всё равно требуется отсканировать.";
        }
        catch (MobileApiException exception)
        {
            SourceHintsStatusLabel.Text = exception.Message;
        }
        catch (HttpRequestException)
        {
            SourceHintsStatusLabel.Text = "Не удалось обновить доступность источников.";
        }
    }

    private async Task ResolveSourceAsync(string barcode)
    {
        SetBusy(true);
        ErrorLabel.Text = string.Empty;
        try
        {
            var source = await _apiClient.ResolveStorageLocationAsync(
                barcode,
                Details.Order.WarehouseId,
                MobileStorageLocationContext.Storage);
            await LoadSourceHintsAsync();
            var availability = SourceHints.SingleOrDefault(x => x.Source.Id == source.Id);
            if (availability is null || availability.AvailableQuantity <= 0)
            {
                ErrorLabel.Text = "В отсканированной позиции нет доступного остатка выбранного товара.";
                return;
            }

            _source = source;
            _sourceAvailability = availability;
            _sourceBarcode = barcode;
            SelectedSourceLabel.Text = $"Источник: {source.Address} · {source.Name}";
            AvailabilityLabel.Text = $"Физически: {availability.PhysicalQuantity:0.###} · "
                + $"Черновики ордера: {availability.DraftQuantity:0.###} · "
                + $"Доступно: {availability.AvailableQuantity:0.###}";
            var initialQuantity = Math.Min(
                _selectedLine!.RemainingQuantity,
                availability.AvailableQuantity);
            QuantityEntry.Text = initialQuantity.ToString("0.###", CultureInfo.InvariantCulture);
            SetMode(MovementPageMode.Quantity);
            Dispatcher.Dispatch(() => QuantityEntry.Focus());
        }
        catch (MobileApiException exception)
        {
            ErrorLabel.Text = exception.Message;
        }
        catch (HttpRequestException)
        {
            ErrorLabel.Text = "Сервер WMS недоступен. Повторите сканирование источника.";
        }
        finally
        {
            SetBusy(false);
            await UpdateCameraAsync();
        }
    }

    private async void OnQuantityNextClicked(object? sender, EventArgs e)
    {
        if (_busy || _selectedLine is null || _source is null)
        {
            return;
        }

        if (_pendingRequestId is null && !TryReadQuantity(out _quantity))
        {
            return;
        }

        QuantityEntry.Unfocus();
        await QuantityEntry.HideSoftInputAsync(CancellationToken.None);
        await SubmitMovementAsync();
    }

    private bool TryReadQuantity(out double quantity)
    {
        var value = (QuantityEntry.Text ?? string.Empty).Trim().Replace(',', '.');
        var maximum = Math.Min(
            _selectedLine?.RemainingQuantity ?? 0,
            _sourceAvailability?.AvailableQuantity ?? 0);
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out quantity)
            && double.IsFinite(quantity)
            && quantity > 0
            && quantity <= maximum)
        {
            return true;
        }

        ErrorLabel.Text = $"Количество должно быть больше нуля и не превышать {maximum:0.###}.";
        return false;
    }

    private async void OnRescanSourceClicked(object? sender, EventArgs e)
    {
        if (_busy || _pendingRequestId is not null)
        {
            return;
        }

        QuantityEntry.Unfocus();
        await QuantityEntry.HideSoftInputAsync(CancellationToken.None);
        ClearSource();
        SetMode(MovementPageMode.SourceScanning);
        await LoadSourceHintsAsync();
        await UpdateCameraAsync();
    }

    private async Task SubmitMovementAsync()
    {
        if (_busy
            || _selectedLine is null
            || _sourceBarcode is null
            || _quantity <= 0)
        {
            return;
        }

        _pendingRequestId ??= Guid.NewGuid();
        SetBusy(true);
        ErrorLabel.Text = string.Empty;
        try
        {
            var response = await _apiClient.AddShippingOrderPickingMovementAsync(
                Details.Order.Id,
                _selectedLine.LineNumber,
                _sourceBarcode,
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
            ConfirmMovementButton.Text = "Отобрать";
            ErrorLabel.Text = exception.Message;
        }
        catch (HttpRequestException)
        {
            ConfirmMovementButton.Text = "Повторить";
            ErrorLabel.Text = "Ответ сервера не получен. Повторите подтверждение этого же движения.";
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
            ErrorLabel.Text = "Сначала повторите подтверждение этого же движения.";
            return;
        }

        await Navigation.PopAsync();
    }

    private void ClearSource()
    {
        _source = null;
        _sourceAvailability = null;
        _sourceBarcode = null;
        SelectedSourceLabel.Text = string.Empty;
        AvailabilityLabel.Text = string.Empty;
    }

    private void SetMode(MovementPageMode mode)
    {
        _mode = mode;
        CandidatePanel.IsVisible = mode == MovementPageMode.CandidateSelection;
        SourceHintsPanel.IsVisible = mode == MovementPageMode.SourceScanning;
        QuantityPanel.IsVisible = mode == MovementPageMode.Quantity;
        (StepLabel.Text, InstructionLabel.Text) = mode switch
        {
            MovementPageMode.SkuScanning => (
                "Товар",
                "Отсканируйте товар с остатком к отбору."),
            MovementPageMode.CandidateSelection => (
                "Строка",
                "Товар найден в нескольких строках. Выберите нужную."),
            MovementPageMode.SourceScanning => (
                "Источник",
                "Отсканируйте позицию-источник в зоне хранения."),
            _ => (
                "Количество",
                "Проверьте количество и нажмите «Отобрать».")
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
        QuantityEntry.IsEnabled = !_busy && _pendingRequestId is null;
        RescanSourceButton.IsEnabled = !_busy && _pendingRequestId is null;
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
        SourceScanning,
        Quantity
    }
}

using System.Globalization;
using Wms.Contracts.Mobile.V1;
using Wms.Mobile.Scanning;
using Wms.Mobile.Services;

namespace Wms.Mobile;

public partial class LicensePlateNumberPutawayPage : ContentPage
{
    private readonly MobileLicensePlateNumberPutawayClient _client;
    private readonly IOperationalBarcodeScanner _scanner;
    private MobileLicensePlateNumberPutawayDetailsResponse? _details;
    private MobileLicensePlateNumberPutawayItemResponse? _selectedItem;
    private IReadOnlyList<MobileLicensePlateNumberPutawayRecommendationResponse> _recommendations = [];
    private PutawayStep _step = PutawayStep.Lpn;
    private bool _busy;
    private bool _isVisible;
    private long _scanGeneration;
    private bool _scannerSubscribed;
    private decimal _quantity;
    private bool _quantityConfirmed;
    private PendingPutaway? _pending;

    public LicensePlateNumberPutawayPage(
        MobileLicensePlateNumberPutawayClient client,
        IOperationalBarcodeScanner scanner)
    {
        InitializeComponent();
        _client = client;
        _scanner = scanner;
        CameraScannerView.Configure(scanner);
        ApplyStep(PutawayStep.Lpn);
    }

    public IReadOnlyList<MobileLicensePlateNumberPutawayItemResponse> Items => _details?.Items ?? [];
    public IReadOnlyList<MobileLicensePlateNumberPutawayHistoryResponse> History => _details?.History ?? [];
    public IReadOnlyList<MobileLicensePlateNumberPutawayRecommendationResponse> CurrentSkuStockRecommendations =>
        Recommendations(MobileLicensePlateNumberPutawayRecommendationCategory.CurrentSkuStock);
    public IReadOnlyList<MobileLicensePlateNumberPutawayRecommendationResponse> EmptyWithSkuHistoryRecommendations =>
        Recommendations(MobileLicensePlateNumberPutawayRecommendationCategory.EmptyWithSkuBalanceHistory);
    public IReadOnlyList<MobileLicensePlateNumberPutawayRecommendationResponse> EmptyRecommendations =>
        Recommendations(MobileLicensePlateNumberPutawayRecommendationCategory.Empty);

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
        Interlocked.Increment(ref _scanGeneration);
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
        if (_busy || _pending is not null)
        {
            ErrorLabel.Text = "Сначала завершите повтор текущего размещения.";
            return true;
        }
        return base.OnBackButtonPressed();
    }

    private void OnScanReceived(object? sender, BarcodeScanEvent scanEvent)
    {
        long generation = Volatile.Read(ref _scanGeneration);
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            if (_isVisible && generation == Volatile.Read(ref _scanGeneration))
                await HandleScanAsync(scanEvent.Value);
        });
    }

    private async Task HandleScanAsync(string barcode)
    {
        if (!_isVisible || _busy || _pending is not null)
            return;

        if (_step == PutawayStep.Lpn)
            await ResolveLpnAsync(barcode);
        else if (_step == PutawayStep.Sku)
            await ResolveSkuAsync(barcode);
        else if (_step == PutawayStep.Quantity)
        {
            if (CanScanDestination)
                await PutawayAsync(barcode);
            else
                ErrorLabel.Text = "Завершите ввод количества или выберите «Весь остаток», затем повторите скан позиции.";
        }
    }

    private async Task ResolveLpnAsync(string code)
    {
        SetBusy(true);
        ErrorLabel.Text = string.Empty;
        try
        {
            ApplyDetails(await _client.ResolveAsync(code));
        }
        catch (MobileApiException exception) { ErrorLabel.Text = exception.Message; }
        catch (HttpRequestException) { ErrorLabel.Text = "Сервер WMS недоступен. Повторите сканирование LPN."; }
        finally { SetBusy(false); await UpdateCameraAsync(); }
    }

    private async Task ResolveSkuAsync(string barcode)
    {
        if (_details is null) return;
        SetBusy(true);
        ErrorLabel.Text = string.Empty;
        try
        {
            SelectItem(await _client.ResolveSkuAsync(_details.Id, barcode));
        }
        catch (MobileApiException exception) { ErrorLabel.Text = exception.Message; }
        catch (HttpRequestException) { ErrorLabel.Text = "Сервер WMS недоступен. Повторите сканирование товара."; }
        finally { SetBusy(false); await UpdateCameraAsync(); }
    }

    private async void OnItemTapped(object? sender, TappedEventArgs e)
    {
        if (_busy || _step != PutawayStep.Sku
            || e.Parameter is not MobileLicensePlateNumberPutawayItemResponse { RemainingQuantity: > 0 } item)
            return;
        SetBusy(true);
        ErrorLabel.Text = string.Empty;
        try
        {
            SelectItem(await _client.GetSelectionAsync(_details!.Id, item.Id));
        }
        catch (MobileApiException exception) { ErrorLabel.Text = exception.Message; }
        catch (HttpRequestException) { ErrorLabel.Text = "Сервер WMS недоступен. Повторите выбор товара."; }
        finally { SetBusy(false); await UpdateCameraAsync(); }
    }

    private void SelectItem(MobileLicensePlateNumberPutawaySelectionResponse selection)
    {
        var item = selection.Item;
        _selectedItem = item;
        _recommendations = selection.Recommendations;
        OnPropertyChanged(nameof(CurrentSkuStockRecommendations));
        OnPropertyChanged(nameof(EmptyWithSkuHistoryRecommendations));
        OnPropertyChanged(nameof(EmptyRecommendations));
        SelectedSkuLabel.Text = item.SkuName;
        RemainingLabel.Text = $"Осталось {item.RemainingQuantity:0.###} {item.UnitOfMeasure}";
        _quantityConfirmed = false;
        _quantity = 0;
        QuantityEntry.Text = string.Empty;
        ApplyStep(PutawayStep.Quantity);
    }

    private bool CanScanDestination => _step == PutawayStep.Quantity
        && _quantityConfirmed && !QuantityEntry.IsFocused
        && _details?.CanPutaway == true && _selectedItem is not null
        && _quantity > 0 && _quantity <= _selectedItem.RemainingQuantity;

    private async void OnQuantityFocused(object? sender, FocusEventArgs e)
    {
        InvalidateQuantity();
        await UpdateCameraAsync();
    }

    private async void OnQuantityTextChanged(object? sender, TextChangedEventArgs e)
    {
        InvalidateQuantity();
        await UpdateCameraAsync();
    }

    private void InvalidateQuantity()
    {
        _quantityConfirmed = false;
        Interlocked.Increment(ref _scanGeneration);
        UpdateQuantityStatus();
    }

    private async void OnQuantityCompleted(object? sender, EventArgs e)
    {
        QuantityEntry.Unfocus();
        await ConfirmQuantityAsync();
    }

    private async void OnQuantityUnfocused(object? sender, FocusEventArgs e) =>
        await ConfirmQuantityAsync();

    private async void OnEntireRemainderClicked(object? sender, EventArgs e)
    {
        if (_busy || _pending is not null || _step != PutawayStep.Quantity || _selectedItem is null)
            return;
        QuantityEntry.Text = _selectedItem.RemainingQuantity.ToString("0.###", CultureInfo.InvariantCulture);
        QuantityEntry.Unfocus();
        await ConfirmQuantityAsync();
    }

    private async Task ConfirmQuantityAsync()
    {
        if (!_isVisible || _busy || _pending is not null || _step != PutawayStep.Quantity
            || _selectedItem is null || QuantityEntry.IsFocused)
            return;
        InvalidateQuantity();
        var text = (QuantityEntry.Text ?? string.Empty).Trim().Replace(',', '.');
        if (!decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var quantity)
            || !WarehouseQuantityInput.IsSupported(quantity)
            || quantity <= 0 || quantity > _selectedItem.RemainingQuantity
            || _details?.CanPutaway != true)
        {
            ErrorLabel.Text = $"Введите количество больше нуля, не более {_selectedItem.RemainingQuantity:0.###} и не более трёх знаков после запятой.";
        }
        else
        {
            _quantity = quantity;
            _quantityConfirmed = true;
            ErrorLabel.Text = string.Empty;
        }
        UpdateQuantityStatus();
        await QuantityEntry.HideSoftInputAsync(CancellationToken.None);
        await UpdateCameraAsync();
    }

    private void UpdateQuantityStatus()
    {
        QuantityStatusLabel.Text = _quantityConfirmed
            ? $"Подтверждено: {_quantity:0.###} {_selectedItem?.UnitOfMeasure}. Скан позиции сразу проводит движение."
            : "Количество не подтверждено. Завершите ввод или выберите «Весь остаток».";
    }

    private async Task PutawayAsync(string destinationBarcode)
    {
        if (!_isVisible || _busy || _pending is not null || !CanScanDestination
            || _details is null || _selectedItem is null) return;
        _pending = new(_details.Id, _selectedItem.Id, _quantity, destinationBarcode, Guid.NewGuid());
        await ExecutePendingPutawayAsync();
    }

    private async void OnRetryClicked(object? sender, EventArgs e) =>
        await ExecutePendingPutawayAsync();

    private async Task ExecutePendingPutawayAsync()
    {
        if (_busy || _pending is not { } pending)
            return;

        SetBusy(true);
        ErrorLabel.Text = string.Empty;
        try
        {
            var response = await _client.PutawayAsync(
                pending.LpnId, pending.ItemId, pending.Quantity,
                pending.DestinationBarcode, pending.RequestId);
            _pending = null;
            RetryButton.IsVisible = false;
            _selectedItem = null;
            InvalidateQuantity();
            ClearRecommendations();
            ApplyDetails(response.Details);
        }
        catch (MobileApiException exception)
        {
            _pending = null;
            RetryButton.IsVisible = false;
            ErrorLabel.Text = exception.Message;
            if (exception.StatusCode == System.Net.HttpStatusCode.Conflict)
                await RefreshConflictStateAsync();
        }
        catch (HttpRequestException)
        {
            RetryButton.IsVisible = true;
            ErrorLabel.Text = "Ответ сервера не получен. Повторите то же размещение.";
        }
        finally { SetBusy(false); await UpdateCameraAsync(); }
    }

    private async Task RefreshConflictStateAsync()
    {
        if (_details is null || _selectedItem is null)
            return;
        try
        {
            _details = await _client.GetAsync(_details.Id);
            _selectedItem = _details.Items.SingleOrDefault(x => x.Id == _selectedItem.Id);
            OnPropertyChanged(nameof(Items));
            OnPropertyChanged(nameof(History));
            if (_selectedItem is not null)
            {
                RemainingLabel.Text = $"Осталось {_selectedItem.RemainingQuantity:0.###} {_selectedItem.UnitOfMeasure}";
            }
            if (_selectedItem is null || !_details.CanPutaway || _selectedItem.RemainingQuantity <= 0)
            {
                InvalidateQuantity();
                _selectedItem = null;
                ClearRecommendations();
                ApplyDetails(_details);
            }
            else if (_quantity > _selectedItem.RemainingQuantity)
                InvalidateQuantity();
        }
        catch
        {
            // The original business error remains actionable even if refresh fails.
        }
    }

    private async void OnNextLpnClicked(object? sender, EventArgs e)
    {
        if (_busy || _pending is not null) return;
        _details = null;
        _selectedItem = null;
        ClearRecommendations();
        OnPropertyChanged(nameof(Items));
        OnPropertyChanged(nameof(History));
        ErrorLabel.Text = string.Empty;
        ApplyStep(PutawayStep.Lpn);
        await UpdateCameraAsync();
    }

    private void ApplyDetails(MobileLicensePlateNumberPutawayDetailsResponse details)
    {
        _details = details;
        LpnCodeLabel.Text = details.Code;
        OrderLabel.Text = $"Ордер: {details.OrderNumber}";
        LpnStatusLabel.Text = details.IsCompleted
            ? "LPN полностью размещён."
            : details.BlockedReason ?? "Выберите или отсканируйте SKU.";
        OnPropertyChanged(nameof(Items));
        OnPropertyChanged(nameof(History));
        ApplyStep(details.CanPutaway ? PutawayStep.Sku : PutawayStep.Status);
    }

    private void ApplyStep(PutawayStep step)
    {
        Interlocked.Increment(ref _scanGeneration);
        _step = step;
        LpnPanel.IsVisible = step is PutawayStep.Sku or PutawayStep.Status;
        QuantityPanel.IsVisible = step == PutawayStep.Quantity;
        HistoryPanel.IsVisible = step == PutawayStep.Status && History.Count > 0;
        NextLpnButton.IsVisible = step == PutawayStep.Status;
        (StepLabel.Text, InstructionLabel.Text) = step switch
        {
            PutawayStep.Lpn => ("LPN", "Отсканируйте LPN."),
            PutawayStep.Sku => ("Товар", "Отсканируйте SKU или выберите строку LPN."),
            PutawayStep.Quantity => ("Количество и позиция", "Подтвердите количество, затем отсканируйте позицию хранения."),
            _ => ("Размещение", "Просмотрите состояние и историю LPN.")
        };
        UpdateQuantityStatus();
    }

    private async Task UpdateCameraAsync()
    {
        if (_isVisible && !_busy && _pending is null
            && _scanner.ActiveSource == BarcodeScanSource.Camera
            && (_step is PutawayStep.Lpn or PutawayStep.Sku || CanScanDestination))
            await CameraScannerView.StartAsync();
        else
            CameraScannerView.Stop();
    }

    private void SetBusy(bool busy)
    {
        Interlocked.Increment(ref _scanGeneration);
        _busy = busy;
        ProgressIndicator.Opacity = busy ? 1 : 0;
        RetryButton.IsVisible = _pending is not null;
        RetryButton.IsEnabled = !busy;
        QuantityEntry.IsEnabled = !busy && _pending is null;
        EntireRemainderButton.IsEnabled = QuantityEntry.IsEnabled;
        NavigationPage.SetHasBackButton(this, !busy && _pending is null);
    }

    private IReadOnlyList<MobileLicensePlateNumberPutawayRecommendationResponse> Recommendations(
        MobileLicensePlateNumberPutawayRecommendationCategory category) =>
        _recommendations.Where(x => x.Category == category).ToList();

    private void ClearRecommendations()
    {
        _recommendations = [];
        OnPropertyChanged(nameof(CurrentSkuStockRecommendations));
        OnPropertyChanged(nameof(EmptyWithSkuHistoryRecommendations));
        OnPropertyChanged(nameof(EmptyRecommendations));
    }

    private sealed record PendingPutaway(
        Guid LpnId, Guid ItemId, decimal Quantity, string DestinationBarcode, Guid RequestId);

    private void OnNonScanControlLoaded(object? sender, EventArgs e)
    {
        if (sender is VisualElement element)
            AndroidFocus.Suppress(element);
    }

    private enum PutawayStep { Lpn, Sku, Quantity, Status }
}

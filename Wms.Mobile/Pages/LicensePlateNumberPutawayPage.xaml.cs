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
    private bool _scannerSubscribed;
    private decimal _quantity;
    private Guid? _pendingRequestId;
    private string? _pendingDestinationBarcode;

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
            ErrorLabel.Text = "Сначала завершите повтор текущего размещения.";
            return true;
        }
        return base.OnBackButtonPressed();
    }

    private void OnScanReceived(object? sender, BarcodeScanEvent e) =>
        MainThread.BeginInvokeOnMainThread(async () => await HandleScanAsync(e.Value));

    private async Task HandleScanAsync(string barcode)
    {
        if (_busy || _pendingRequestId is not null)
            return;

        if (_step == PutawayStep.Lpn)
            await ResolveLpnAsync(barcode);
        else if (_step == PutawayStep.Sku)
            await ResolveSkuAsync(barcode);
        else if (_step == PutawayStep.Destination)
            await PutawayAsync(barcode);
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
        QuantityEntry.Text = item.RemainingQuantity.ToString("0.###", CultureInfo.InvariantCulture);
        ApplyStep(PutawayStep.Quantity);
    }

    private async void OnQuantityConfirmed(object? sender, EventArgs e)
    {
        if (_busy || _selectedItem is null) return;
        var text = (QuantityEntry.Text ?? string.Empty).Trim().Replace(',', '.');
        if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out _quantity)
            || !WarehouseQuantityInput.IsSupported(_quantity)
            || _quantity <= 0 || _quantity > _selectedItem.RemainingQuantity)
        {
            ErrorLabel.Text = $"Количество должно быть больше нуля и не превышать {_selectedItem.RemainingQuantity:0.###}.";
            return;
        }
        QuantityEntry.Unfocus();
        await QuantityEntry.HideSoftInputAsync(CancellationToken.None);
        DestinationSkuLabel.Text = _selectedItem.SkuName;
        DestinationQuantityLabel.Text = $"Количество: {_quantity:0.###} {_selectedItem.UnitOfMeasure}";
        ErrorLabel.Text = string.Empty;
        ApplyStep(PutawayStep.Destination);
        await UpdateCameraAsync();
    }

    private async Task PutawayAsync(string destinationBarcode)
    {
        if (_details is null || _selectedItem is null) return;
        _pendingRequestId = Guid.NewGuid();
        _pendingDestinationBarcode = destinationBarcode;
        await ExecutePendingPutawayAsync();
    }

    private async void OnRetryClicked(object? sender, EventArgs e) =>
        await ExecutePendingPutawayAsync();

    private async Task ExecutePendingPutawayAsync()
    {
        if (_busy || _details is null || _selectedItem is null
            || _pendingRequestId is not Guid requestId
            || _pendingDestinationBarcode is not string destinationBarcode)
            return;

        SetBusy(true);
        ErrorLabel.Text = string.Empty;
        try
        {
            var response = await _client.PutawayAsync(
                _details.Id, _selectedItem.Id, _quantity,
                destinationBarcode, requestId);
            _pendingRequestId = null;
            _pendingDestinationBarcode = null;
            RetryButton.IsVisible = false;
            _selectedItem = null;
            ClearRecommendations();
            ApplyDetails(response.Details);
        }
        catch (MobileApiException exception)
        {
            _pendingRequestId = null;
            _pendingDestinationBarcode = null;
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
                DestinationQuantityLabel.Text =
                    $"Выбрано: {_quantity:0.###}; актуальный остаток: {_selectedItem.RemainingQuantity:0.###} {_selectedItem.UnitOfMeasure}";
            }
        }
        catch
        {
            // The original business error remains actionable even if refresh fails.
        }
    }

    private async void OnChangeQuantityClicked(object? sender, EventArgs e)
    {
        if (_busy || _pendingRequestId is not null) return;
        ApplyStep(PutawayStep.Quantity);
        await UpdateCameraAsync();
    }

    private async void OnNextLpnClicked(object? sender, EventArgs e)
    {
        if (_busy || _pendingRequestId is not null) return;
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
        _step = step;
        LpnPanel.IsVisible = step is PutawayStep.Sku or PutawayStep.Status;
        QuantityPanel.IsVisible = step == PutawayStep.Quantity;
        DestinationPanel.IsVisible = step == PutawayStep.Destination;
        HistoryPanel.IsVisible = step == PutawayStep.Status && History.Count > 0;
        NextLpnButton.IsVisible = step == PutawayStep.Status;
        (StepLabel.Text, InstructionLabel.Text) = step switch
        {
            PutawayStep.Lpn => ("LPN", "Отсканируйте LPN."),
            PutawayStep.Sku => ("Товар", "Отсканируйте SKU или выберите строку LPN."),
            PutawayStep.Quantity => ("Количество", "Указан весь остаток LPN. При необходимости уменьшите его."),
            PutawayStep.Destination => ("Позиция назначения", "Отсканируйте активную позицию хранения."),
            _ => ("Размещение", "Просмотрите состояние и историю LPN.")
        };
    }

    private async Task UpdateCameraAsync()
    {
        if (_isVisible && !_busy && _pendingRequestId is null
            && _scanner.ActiveSource == BarcodeScanSource.Camera
            && _step is PutawayStep.Lpn or PutawayStep.Sku or PutawayStep.Destination)
            await CameraScannerView.StartAsync();
        else
            CameraScannerView.Stop();
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        ProgressIndicator.Opacity = busy ? 1 : 0;
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

    private enum PutawayStep { Lpn, Sku, Quantity, Destination, Status }
}

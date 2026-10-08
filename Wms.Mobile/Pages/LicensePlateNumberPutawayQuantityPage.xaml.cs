using System.Globalization;
using Wms.Contracts.Mobile.V1;
using Wms.Mobile.Scanning;
using Wms.Mobile.Services;

namespace Wms.Mobile;

public partial class LicensePlateNumberPutawayQuantityPage : ContentPage
{
    private readonly MobileLicensePlateNumberPutawayClient _client;
    private readonly IOperationalBarcodeScanner _scanner;
    private MobileLicensePlateNumberPutawayDetailsResponse? _details;
    private MobileLicensePlateNumberPutawayItemResponse? _item;
    private LicensePlateNumberPutawayDetailsPage? _detailsPage;
    private IReadOnlyList<MobileLicensePlateNumberPutawayRecommendationResponse> _recommendations = [];
    private bool _isVisible;
    private bool _busy;
    private bool _scannerSubscribed;
    private long _scanGeneration;
    private decimal _quantity;
    private bool _quantityConfirmed;
    private PendingPutaway? _pending;

    public LicensePlateNumberPutawayQuantityPage(
        MobileLicensePlateNumberPutawayClient client,
        IOperationalBarcodeScanner scanner)
    {
        InitializeComponent();
        _client = client;
        _scanner = scanner;
        ErrorLabel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(Label.Text))
                ErrorLabel.IsVisible = !string.IsNullOrWhiteSpace(ErrorLabel.Text);
        };
        CameraScannerView.Configure(scanner);
    }

    public IReadOnlyList<MobileLicensePlateNumberPutawayRecommendationResponse> CurrentSkuStockRecommendations =>
        Recommendations(MobileLicensePlateNumberPutawayRecommendationCategory.CurrentSkuStock);
    public IReadOnlyList<MobileLicensePlateNumberPutawayRecommendationResponse> EmptyWithSkuHistoryRecommendations =>
        Recommendations(MobileLicensePlateNumberPutawayRecommendationCategory.EmptyWithSkuBalanceHistory);

    public void Show(
        MobileLicensePlateNumberPutawayDetailsResponse details,
        MobileLicensePlateNumberPutawaySelectionResponse selection,
        LicensePlateNumberPutawayDetailsPage detailsPage)
    {
        _details = details;
        _item = selection.Item;
        _detailsPage = detailsPage;
        _recommendations = selection.Recommendations;
        OnPropertyChanged(nameof(CurrentSkuStockRecommendations));
        OnPropertyChanged(nameof(EmptyWithSkuHistoryRecommendations));
        SelectedSkuLabel.Text = _item.SkuName;
        CurrentStockHeadingLabel.Text = $"1. Ячейки с текущим остатком SKU ({_item.UnitOfMeasure ?? "—"})";
        UpdateRemainder();
        _quantity = 0;
        _quantityConfirmed = false;
        QuantityEntry.Text = string.Empty;
        UpdateQuantityStatus();
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
        if (!_busy && _pending is null) return base.OnBackButtonPressed();
        ErrorLabel.Text = "Сначала завершите повтор текущего размещения.";
        return true;
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
        if (!_isVisible || _busy || _pending is not null) return;
        if (CanScanDestination)
            await PutawayAsync(barcode);
        else
            ErrorLabel.Text = "Завершите ввод количества или выберите «Весь остаток», затем повторите скан ячейки.";
    }

    private bool CanScanDestination => _quantityConfirmed && !QuantityEntry.IsFocused
        && _details?.CanPutaway == true && _item is not null
        && _quantity > 0 && _quantity <= _item.RemainingQuantity;

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
        if (_busy || _pending is not null || _item is null) return;
        QuantityEntry.Text = _item.RemainingQuantity.ToString("0.###", CultureInfo.InvariantCulture);
        QuantityEntry.Unfocus();
        await ConfirmQuantityAsync();
    }

    private async Task ConfirmQuantityAsync()
    {
        if (!_isVisible || _busy || _pending is not null || _item is null || QuantityEntry.IsFocused)
            return;
        InvalidateQuantity();
        var text = (QuantityEntry.Text ?? string.Empty).Trim().Replace(',', '.');
        if (!decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var quantity)
            || !WarehouseQuantityInput.IsSupported(quantity)
            || quantity <= 0 || quantity > _item.RemainingQuantity
            || _details?.CanPutaway != true)
        {
            ErrorLabel.Text = $"Введите количество больше нуля, не более {_item.RemainingQuantity:0.###} и не более трёх знаков после запятой.";
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
        QuantityStatusLabel.IsVisible = _quantityConfirmed;
        QuantityStatusLabel.Text = _quantityConfirmed ? $"Подтверждено: {_quantity:0.###}" : string.Empty;
        ScanCellHintLabel.Text = _quantityConfirmed
            ? "Просканируйте ячейку для размещения товара."
            : "Завершите ввод количества или выберите «Весь остаток».";
    }

    private void UpdateRemainder()
    {
        if (_item is null) return;
        RemainingLabel.Text = $"Осталось {_item.RemainingQuantity:0.###} {_item.UnitOfMeasure ?? "—"}";
        EntireRemainderButton.Text = $"Весь остаток: {_item.RemainingQuantity:0.###}";
    }

    private async Task PutawayAsync(string barcode)
    {
        if (!_isVisible || _busy || _pending is not null || !CanScanDestination
            || _details is null || _item is null) return;
        _pending = new(_details.Id, _item.Id, _quantity, barcode, Guid.NewGuid());
        await ExecutePendingPutawayAsync();
    }

    private async void OnRetryClicked(object? sender, EventArgs e) =>
        await ExecutePendingPutawayAsync();

    private async Task ExecutePendingPutawayAsync()
    {
        if (_busy || _pending is not { } pending) return;
        SetBusy(true);
        ErrorLabel.Text = string.Empty;
        try
        {
            var response = await _client.PutawayAsync(
                pending.LpnId, pending.ItemId, pending.Quantity,
                pending.DestinationBarcode, pending.RequestId);
            _pending = null;
            _detailsPage?.Show(response.Details);
            await Navigation.PopAsync();
        }
        catch (MobileApiException exception)
        {
            _pending = null;
            ErrorLabel.Text = exception.Message;
            if (exception.StatusCode == System.Net.HttpStatusCode.Conflict)
                await RefreshConflictStateAsync();
        }
        catch (HttpRequestException)
        {
            ErrorLabel.Text = "Ответ сервера не получен. Повторите то же размещение.";
        }
        finally { SetBusy(false); await UpdateCameraAsync(); }
    }

    private async Task RefreshConflictStateAsync()
    {
        if (_details is null || _item is null) return;
        try
        {
            _details = await _client.GetAsync(_details.Id);
            _item = _details.Items.SingleOrDefault(x => x.Id == _item.Id);
            UpdateRemainder();
            if (_item is null || !_details.CanPutaway || _item.RemainingQuantity <= 0)
            {
                _detailsPage?.Show(_details);
                await Navigation.PopAsync();
            }
            else if (_quantity > _item.RemainingQuantity)
                InvalidateQuantity();
        }
        catch
        {
            // Keep the original business error when refreshing the state fails.
        }
    }

    private async Task UpdateCameraAsync()
    {
        if (_isVisible && !_busy && _pending is null && CanScanDestination
            && _scanner.ActiveSource == BarcodeScanSource.Camera)
            await CameraScannerView.StartAsync();
        else
            CameraScannerView.Stop();
    }

    private void SetBusy(bool busy)
    {
        Interlocked.Increment(ref _scanGeneration);
        _busy = busy;
        ProgressIndicator.IsVisible = busy;
        RetryButton.IsVisible = _pending is not null;
        RetryButton.IsEnabled = !busy;
        QuantityEntry.IsEnabled = !busy && _pending is null;
        EntireRemainderButton.IsEnabled = QuantityEntry.IsEnabled;
        NavigationPage.SetHasBackButton(this, !busy && _pending is null);
    }

    private IReadOnlyList<MobileLicensePlateNumberPutawayRecommendationResponse> Recommendations(
        MobileLicensePlateNumberPutawayRecommendationCategory category) =>
        _recommendations.Where(x => x.Category == category).ToList();

    private void OnNonScanControlLoaded(object? sender, EventArgs e)
    {
        if (sender is VisualElement element)
            AndroidFocus.Suppress(element);
    }

    private sealed record PendingPutaway(
        Guid LpnId, Guid ItemId, decimal Quantity, string DestinationBarcode, Guid RequestId);
}

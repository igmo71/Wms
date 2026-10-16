using System.Globalization;
using Wms.Contracts.Mobile.V1;
using Wms.Mobile.Scanning;
using Wms.Mobile.Services;

namespace Wms.Mobile;

public partial class LicensePlateNumberPutawayQuantityPage : ContentPage
{
    private readonly MobileLicensePlateNumberPutawayClient _client;
    private readonly MobileReferenceDataClient _reference;
    private readonly IOperationalBarcodeScanner _scanner;
    private MobileReceivingWorkLpnResponse? _details;
    private MobileReceivingWorkLpnItemResponse? _item;
    private IReadOnlyList<MobileReceivingPutawayRecommendationResponse> _recommendations = [];
    private bool _isVisible;
    private bool _busy;
    private bool _scannerSubscribed;
    private long _scanGeneration;
    private decimal _quantity;
    private bool _quantityConfirmed;
    private PendingPutaway? _pending;

    public LicensePlateNumberPutawayQuantityPage(
        MobileLicensePlateNumberPutawayClient client,
        MobileReferenceDataClient reference,
        IOperationalBarcodeScanner scanner)
    {
        InitializeComponent();
        _client = client;
        _reference = reference;
        _scanner = scanner;
        ErrorLabel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(Label.Text))
                ErrorLabel.IsVisible = !string.IsNullOrWhiteSpace(ErrorLabel.Text);
        };
        CameraScannerView.Configure(scanner);
    }

    public IReadOnlyList<MobileReceivingPutawayRecommendationResponse> CurrentSkuStockRecommendations =>
        Recommendations(MobileReceivingPutawayRecommendationKind.CurrentSkuStock);
    public IReadOnlyList<MobileReceivingPutawayRecommendationResponse> EmptyWithSkuHistoryRecommendations =>
        Recommendations(MobileReceivingPutawayRecommendationKind.EmptyWithSkuBalanceHistory);

    public void Show(
        MobileReceivingWorkLpnResponse details,
        MobileReceivingPutawaySelectionResponse selection)
    {
        _details = details;
        _item = selection.Item;
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
        && _details?.Status == MobileLicensePlateNumberStatus.Closed && _item is not null
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
            || _details?.Status != MobileLicensePlateNumberStatus.Closed)
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
        SetBusy(true);
        try
        {
            var location = await _reference.ResolveStorageLocationAsync(
                barcode, _details.WarehouseId, MobileStorageLocationContext.Storage);
            _pending = new(_details.Id, _item.Id, _quantity, location.Id, Guid.NewGuid());
        }
        catch (MobileApiException exception) { ErrorLabel.Text = exception.Message; }
        catch (HttpRequestException) { ErrorLabel.Text = "Не удалось проверить ячейку хранения."; }
        finally { SetBusy(false); }
        if (_pending is null) return;
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
            await _client.PutawayAsync(
                pending.LpnId, pending.ItemId, pending.Quantity,
                pending.DestinationLocationId, pending.RequestId);
            _pending = null;
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
            if (_item is null || _details.Status != MobileLicensePlateNumberStatus.Closed
                || _item.RemainingQuantity <= 0)
            {
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

    private IReadOnlyList<MobileReceivingPutawayRecommendationResponse> Recommendations(
        MobileReceivingPutawayRecommendationKind category) =>
        _recommendations.Where(x => x.Kind == category).ToList();

    private void OnNonScanControlLoaded(object? sender, EventArgs e)
    {
        if (sender is VisualElement element)
            AndroidFocus.Suppress(element);
    }

    private sealed record PendingPutaway(
        Guid LpnId, Guid ItemId, decimal Quantity, Guid DestinationLocationId, Guid RequestId);
}

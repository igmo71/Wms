using Microsoft.Extensions.DependencyInjection;
using Wms.Contracts.Mobile.V1;
using Wms.Mobile.Scanning;
using Wms.Mobile.Services;

namespace Wms.Mobile;

public partial class LicensePlateNumberPutawayDetailsPage : ContentPage
{
    private readonly MobileLicensePlateNumberPutawayClient _client;
    private readonly IOperationalBarcodeScanner _scanner;
    private readonly IServiceProvider _services;
    private MobileLicensePlateNumberPutawayDetailsResponse? _details;
    private bool _hasAppeared;
    private bool _isVisible;
    private bool _busy;
    private bool _scannerSubscribed;
    private long _scanGeneration;

    public LicensePlateNumberPutawayDetailsPage(
        MobileLicensePlateNumberPutawayClient client,
        IOperationalBarcodeScanner scanner,
        IServiceProvider services)
    {
        InitializeComponent();
        _client = client;
        _scanner = scanner;
        _services = services;
        ErrorLabel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(Label.Text))
                ErrorLabel.IsVisible = !string.IsNullOrWhiteSpace(ErrorLabel.Text);
        };
        CameraScannerView.Configure(scanner);
    }

    public IReadOnlyList<MobileLicensePlateNumberPutawayItemResponse> Items => _details?.Items ?? [];
    public IReadOnlyList<MobileLicensePlateNumberPutawayHistoryResponse> History => _details?.History ?? [];

    public void Show(MobileLicensePlateNumberPutawayDetailsResponse details)
    {
        _details = details;
        Title = details.Code;
        CompletedLabel.IsVisible = details.IsCompleted && details.BlockedReason is null;
        LpnStatusLabel.Text = details.BlockedReason;
        LpnStatusLabel.IsVisible = !string.IsNullOrWhiteSpace(details.BlockedReason);
        HistoryPanel.IsVisible = !details.CanPutaway && details.History.Count > 0;
        NextLpnButton.IsVisible = !details.CanPutaway;
        OnPropertyChanged(nameof(Items));
        OnPropertyChanged(nameof(History));
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
        if (_hasAppeared && _details is not null)
            await RefreshAsync();
        _hasAppeared = true;
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
        if (!_busy) return base.OnBackButtonPressed();
        ErrorLabel.Text = "Дождитесь результата операции.";
        return true;
    }

    private void OnScanReceived(object? sender, BarcodeScanEvent scanEvent)
    {
        long generation = Volatile.Read(ref _scanGeneration);
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            if (_isVisible && generation == Volatile.Read(ref _scanGeneration))
                await SelectByBarcodeAsync(scanEvent.Value);
        });
    }

    private async Task SelectByBarcodeAsync(string barcode)
    {
        if (!_isVisible || _busy || _details?.CanPutaway != true) return;
        SetBusy(true);
        ErrorLabel.Text = string.Empty;
        try
        {
            await OpenQuantityAsync(await _client.ResolveSkuAsync(_details.Id, barcode));
        }
        catch (MobileApiException exception) { ErrorLabel.Text = exception.Message; }
        catch (HttpRequestException) { ErrorLabel.Text = "Сервер WMS недоступен. Повторите сканирование товара."; }
        finally { SetBusy(false); await UpdateCameraAsync(); }
    }

    private async void OnItemTapped(object? sender, TappedEventArgs e)
    {
        if (!_isVisible || _busy || _details?.CanPutaway != true
            || e.Parameter is not MobileLicensePlateNumberPutawayItemResponse { RemainingQuantity: > 0 } item)
            return;
        SetBusy(true);
        ErrorLabel.Text = string.Empty;
        try
        {
            await OpenQuantityAsync(await _client.GetSelectionAsync(_details.Id, item.Id));
        }
        catch (MobileApiException exception) { ErrorLabel.Text = exception.Message; }
        catch (HttpRequestException) { ErrorLabel.Text = "Сервер WMS недоступен. Повторите выбор товара."; }
        finally { SetBusy(false); await UpdateCameraAsync(); }
    }

    private async Task OpenQuantityAsync(MobileLicensePlateNumberPutawaySelectionResponse selection)
    {
        var page = _services.GetRequiredService<LicensePlateNumberPutawayQuantityPage>();
        page.Show(_details!, selection, this);
        await Navigation.PushAsync(page);
    }

    private async Task RefreshAsync()
    {
        if (_details is null) return;
        SetBusy(true);
        ErrorLabel.Text = string.Empty;
        try { Show(await _client.GetAsync(_details.Id)); }
        catch (MobileApiException exception) { ErrorLabel.Text = exception.Message; }
        catch (HttpRequestException) { ErrorLabel.Text = "Не удалось обновить палету. Повторите открытие."; }
        finally { SetBusy(false); }
    }

    private async void OnNextLpnClicked(object? sender, EventArgs e)
    {
        if (!_busy) await Navigation.PopAsync();
    }

    private async Task UpdateCameraAsync()
    {
        if (_isVisible && !_busy && _details?.CanPutaway == true
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
        NextLpnButton.IsEnabled = !busy;
        NavigationPage.SetHasBackButton(this, !busy);
    }

    private void OnNonScanControlLoaded(object? sender, EventArgs e)
    {
        if (sender is VisualElement element)
            AndroidFocus.Suppress(element);
    }
}

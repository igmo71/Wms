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
    private MobileReceivingWorkLpnResponse? _lpn;
    private bool _visible;
    private bool _busy;
    private long _scanGeneration;

    public LicensePlateNumberPutawayDetailsPage(MobileLicensePlateNumberPutawayClient client,
        IOperationalBarcodeScanner scanner, IServiceProvider services)
    {
        InitializeComponent();
        _client = client;
        _scanner = scanner;
        _services = services;
        BindingContext = this;
        CameraScannerView.Configure(scanner);
    }

    public IReadOnlyList<ReceivingPutawayItemRow> Items { get; private set; } = [];
    public IReadOnlyList<ReceivingPutawayHistoryRow> History { get; private set; } = [];

    public void Show(MobileReceivingWorkLpnResponse lpn)
    {
        _lpn = lpn;
        CodeLabel.Text = lpn.Code;
        StatusLabel.Text = lpn.Status == MobileLicensePlateNumberStatus.Closed
            ? "Закрыт: доступен для размещения" : "Размещение доступно только после закрытия LPN";
        Items = lpn.Items.Select(x => new ReceivingPutawayItemRow(x.Id,
            x.SkuName ?? x.SkuId.ToString(),
            $"Принято {x.Quantity:0.###} · размещено {x.PlacedQuantity:0.###} · осталось {x.RemainingQuantity:0.###} {x.UnitOfMeasure ?? "—"} · срок {x.ExpirationDate?.ToString("dd.MM.yyyy") ?? "не указан"}")).ToArray();
        History = lpn.Putaways.Select(x => new ReceivingPutawayHistoryRow(
            $"{x.Quantity:0.###} → {x.DestinationStorageLocationId} · {x.CreatedAtUtc.ToLocalTime():dd.MM.yyyy HH:mm}")).ToArray();
        OnPropertyChanged(nameof(Items));
        OnPropertyChanged(nameof(History));
        NextLpnButton.IsVisible = !lpn.Items.Any(x => x.RemainingQuantity > 0);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _visible = true;
        _scanner.ScanReceived += OnScanReceived;
        if (_lpn is not null) await RefreshAsync();
        await UpdateCameraAsync();
    }

    protected override void OnDisappearing()
    {
        _visible = false;
        Interlocked.Increment(ref _scanGeneration);
        _scanner.ScanReceived -= OnScanReceived;
        CameraScannerView.Stop();
        base.OnDisappearing();
    }

    private void OnScanReceived(object? sender, BarcodeScanEvent scan)
    {
        var generation = Volatile.Read(ref _scanGeneration);
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            if (_visible && generation == Volatile.Read(ref _scanGeneration))
                await ResolveSkuAsync(scan.Value);
        });
    }

    private async Task ResolveSkuAsync(string barcode)
    {
        if (!_visible || _busy || _lpn?.Status != MobileLicensePlateNumberStatus.Closed) return;
        SetBusy(true);
        try
        {
            var selection = await _client.ResolveSkuAsync(_lpn.Id, barcode);
            await OpenQuantityAsync(selection);
        }
        catch (MobileApiException ex) { ErrorLabel.Text = ex.Message; }
        catch (HttpRequestException) { ErrorLabel.Text = "Не удалось определить SKU LPN."; }
        finally { SetBusy(false); await UpdateCameraAsync(); }
    }

    private async void OnItemTapped(object? sender, TappedEventArgs e)
    {
        if (_busy || _lpn?.Status != MobileLicensePlateNumberStatus.Closed
            || e.Parameter is not Guid itemId
            || !_lpn.Items.Any(x => x.Id == itemId && x.RemainingQuantity > 0)) return;
        SetBusy(true);
        try { await OpenQuantityAsync(await _client.GetSelectionAsync(_lpn.Id, itemId)); }
        catch (MobileApiException ex) { ErrorLabel.Text = ex.Message; }
        catch (HttpRequestException) { ErrorLabel.Text = "Не удалось загрузить рекомендации размещения."; }
        finally { SetBusy(false); await UpdateCameraAsync(); }
    }

    private async Task OpenQuantityAsync(MobileReceivingPutawaySelectionResponse selection)
    {
        var page = _services.GetRequiredService<LicensePlateNumberPutawayQuantityPage>();
        page.Show(_lpn!, selection);
        await Navigation.PushAsync(page);
    }

    private async Task RefreshAsync()
    {
        if (_lpn is null || _busy) return;
        SetBusy(true);
        try { Show(await _client.GetAsync(_lpn.Id)); }
        catch (MobileApiException ex) { ErrorLabel.Text = ex.Message; }
        catch (HttpRequestException) { ErrorLabel.Text = "Не удалось обновить LPN."; }
        finally { SetBusy(false); }
    }

    private async void OnNextLpnClicked(object? sender, EventArgs e)
    {
        if (!_busy) await Navigation.PopAsync();
    }

    protected override bool OnBackButtonPressed()
    {
        if (!_busy) return base.OnBackButtonPressed();
        ErrorLabel.Text = "Дождитесь результата операции.";
        return true;
    }

    private void SetBusy(bool busy)
    {
        Interlocked.Increment(ref _scanGeneration);
        _busy = busy;
        ProgressIndicator.IsVisible = busy;
        NextLpnButton.IsEnabled = !busy;
        NavigationPage.SetHasBackButton(this, !busy);
    }

    private async Task UpdateCameraAsync()
    {
        if (_visible && !_busy && _lpn?.Status == MobileLicensePlateNumberStatus.Closed
            && _lpn.Items.Any(x => x.RemainingQuantity > 0)
            && _scanner.ActiveSource == BarcodeScanSource.Camera)
            await CameraScannerView.StartAsync();
        else CameraScannerView.Stop();
    }
}

public sealed record ReceivingPutawayItemRow(Guid Id, string Name, string QuantityText);
public sealed record ReceivingPutawayHistoryRow(string Description);

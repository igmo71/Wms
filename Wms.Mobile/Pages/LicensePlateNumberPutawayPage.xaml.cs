using Microsoft.Extensions.DependencyInjection;
using Wms.Mobile.Scanning;
using Wms.Mobile.Services;

namespace Wms.Mobile;

public partial class LicensePlateNumberPutawayPage : ContentPage
{
    private readonly MobileLicensePlateNumberPutawayClient _client;
    private readonly IOperationalBarcodeScanner _scanner;
    private readonly IServiceProvider _services;
    private bool _isVisible;
    private bool _busy;
    private bool _scannerSubscribed;
    private long _scanGeneration;

    public LicensePlateNumberPutawayPage(
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
        if (!_busy) return base.OnBackButtonPressed();
        ErrorLabel.Text = "Дождитесь результата сканирования.";
        return true;
    }

    private void OnScanReceived(object? sender, BarcodeScanEvent scanEvent)
    {
        long generation = Volatile.Read(ref _scanGeneration);
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            if (_isVisible && generation == Volatile.Read(ref _scanGeneration))
                await ResolveAsync(scanEvent.Value);
        });
    }

    private async Task ResolveAsync(string code)
    {
        if (!_isVisible || _busy) return;
        SetBusy(true);
        ErrorLabel.Text = string.Empty;
        try
        {
            var details = await _client.ResolveAsync(code);
            var page = _services.GetRequiredService<LicensePlateNumberPutawayDetailsPage>();
            page.Show(details);
            await Navigation.PushAsync(page);
        }
        catch (MobileApiException exception) { ErrorLabel.Text = exception.Message; }
        catch (HttpRequestException) { ErrorLabel.Text = "Сервер WMS недоступен. Повторите сканирование LPN."; }
        finally { SetBusy(false); await UpdateCameraAsync(); }
    }

    private async Task UpdateCameraAsync()
    {
        if (_isVisible && !_busy && _scanner.ActiveSource == BarcodeScanSource.Camera)
            await CameraScannerView.StartAsync();
        else
            CameraScannerView.Stop();
    }

    private void SetBusy(bool busy)
    {
        Interlocked.Increment(ref _scanGeneration);
        _busy = busy;
        ProgressIndicator.IsVisible = busy;
        NavigationPage.SetHasBackButton(this, !busy);
    }
}

using Microsoft.Extensions.DependencyInjection;
using Wms.Contracts.Mobile.V1;
using Wms.Mobile.Scanning;
using Wms.Mobile.Services;

namespace Wms.Mobile;

public partial class ReceivingOrderPage : ContentPage
{
    private readonly MobileReceivingOrderClient _orders;
    private readonly MobileReferenceDataClient _reference;
    private readonly MobileWarehouseSelection _selection;
    private readonly IOperationalBarcodeScanner _scanner;
    private readonly IServiceProvider _services;
    private bool _loaded;
    private bool _visible;
    private bool _busy;
    private bool _cameraRequested;
    private long _scanGeneration;

    public ReceivingOrderPage(MobileReceivingOrderClient orders,
        MobileReferenceDataClient reference, MobileWarehouseSelection selection,
        IOperationalBarcodeScanner scanner, IServiceProvider services)
    {
        InitializeComponent();
        _orders = orders;
        _reference = reference;
        _selection = selection;
        _scanner = scanner;
        _services = services;
        BindingContext = this;
        CameraScannerView.Configure(scanner);
    }

    public IReadOnlyList<ReceivingWorkOrderRow> OrderRows { get; private set; } = [];
    private MobileWarehouseResponse? Warehouse => WarehousePicker.SelectedItem as MobileWarehouseResponse;

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _visible = true;
        _scanner.ScanReceived += OnScanReceived;
        CameraButton.IsVisible = _scanner.ActiveSource == BarcodeScanSource.Camera;
        if (!_loaded)
        {
            try
            {
                var warehouses = await _reference.GetWarehousesAsync();
                WarehousePicker.ItemsSource = warehouses.ToList();
                var (index, warning) = _selection.Resolve(warehouses);
                if (index >= 0) WarehousePicker.SelectedIndex = index;
                ErrorLabel.Text = warning ?? string.Empty;
                _loaded = true;
            }
            catch (MobileApiException ex) { ErrorLabel.Text = ex.Message; }
            catch (HttpRequestException) { ErrorLabel.Text = "Сервер WMS недоступен."; }
        }
        await RefreshAsync();
        await UpdateCameraAsync();
    }

    protected override void OnDisappearing()
    {
        _visible = false;
        _cameraRequested = false;
        Interlocked.Increment(ref _scanGeneration);
        _scanner.ScanReceived -= OnScanReceived;
        CameraScannerView.Stop();
        base.OnDisappearing();
    }

    private async void OnWarehouseChanged(object? sender, EventArgs e)
    {
        if (Warehouse is { } warehouse) _selection.Select(warehouse.Id);
        await RefreshAsync();
    }

    private async void OnRefreshing(object? sender, EventArgs e)
    {
        await RefreshAsync();
        QueueRefreshView.IsRefreshing = false;
    }

    private async Task RefreshAsync()
    {
        if (_busy || Warehouse is not { } warehouse) return;
        SetBusy(true);
        try
        {
            var orders = await _orders.GetOrdersAsync(warehouse.Id);
            OrderRows = orders.Select(x =>
            {
                var status = ReceivingStatusSymbols.ForOrder(x.Status);
                return new ReceivingWorkOrderRow(x.Id, x.Number,
                    $"Создан: {x.CreatedAtUtc.ToLocalTime():dd.MM.yyyy HH:mm:ss}",
                    status.Glyph, $"Статус ордера: {status.Description}", status.TextColor);
            }).ToArray();
            OnPropertyChanged(nameof(OrderRows));
            ErrorLabel.Text = string.Empty;
        }
        catch (MobileApiException ex) { ErrorLabel.Text = ex.Message; }
        catch (HttpRequestException) { ErrorLabel.Text = "Не удалось обновить ордера."; }
        finally
        {
            SetBusy(false);
            if (Warehouse is { } current && current.Id != warehouse.Id)
                await RefreshAsync();
            await UpdateCameraAsync();
        }
    }

    private void OnScanReceived(object? sender, BarcodeScanEvent scan)
    {
        var generation = Volatile.Read(ref _scanGeneration);
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            if (_visible && generation == Volatile.Read(ref _scanGeneration))
                await ResolveAsync(scan.Value);
        });
    }

    private async Task ResolveAsync(string barcode)
    {
        if (!_visible || _busy || Warehouse is not { } warehouse) return;
        _cameraRequested = false;
        SetBusy(true);
        ErrorLabel.Text = string.Empty;
        try
        {
            var resolved = await _orders.ResolveDocumentAsync(warehouse.Id, barcode);
            if (resolved.Order is { } linkedOrder)
            {
                await OpenOrderAsync(linkedOrder);
                return;
            }
            if (resolved.Source is { } source)
            {
                var page = _services.GetRequiredService<ReceivingOrderSourcesPage>();
                page.ShowForCreation(warehouse.Id, source.RefKey);
                await Navigation.PushAsync(page);
                return;
            }
            ErrorLabel.Text = "Документ 1С не найден.";
        }
        catch (MobileApiException ex) { ErrorLabel.Text = ex.Message; }
        catch (HttpRequestException) { ErrorLabel.Text = "Сервер WMS недоступен. Повторите сканирование."; }
        finally { SetBusy(false); await UpdateCameraAsync(); }
    }

    private async void OnCameraClicked(object? sender, EventArgs e)
    {
        if (_busy || Warehouse is null || _scanner.ActiveSource != BarcodeScanSource.Camera)
            return;
        _cameraRequested = !_cameraRequested;
        await UpdateCameraAsync();
    }

    private async Task UpdateCameraAsync()
    {
        CameraButton.Text = _cameraRequested ? "Закрыть камеру" : "Сканировать документ 1С";
        if (_visible && !_busy && _cameraRequested && Warehouse is not null
            && _scanner.ActiveSource == BarcodeScanSource.Camera)
            await CameraScannerView.StartAsync();
        else CameraScannerView.Stop();
    }

    private async void OnCreateClicked(object? sender, EventArgs e)
    {
        if (_busy || Warehouse is not { } warehouse) return;
        var page = _services.GetRequiredService<ReceivingOrderSourcesPage>();
        page.ShowForCreation(warehouse.Id);
        await Navigation.PushAsync(page);
    }

    private async void OnOrderTapped(object? sender, TappedEventArgs e)
    {
        if (_busy || e.Parameter is not Guid id) return;
        SetBusy(true);
        try
        {
            await OpenOrderAsync(await _orders.GetAsync(id));
        }
        catch (MobileApiException ex) { ErrorLabel.Text = ex.Message; }
        catch (HttpRequestException) { ErrorLabel.Text = "Сервер WMS недоступен."; }
        finally { SetBusy(false); }
    }

    private async Task OpenOrderAsync(MobileReceivingWorkOrderResponse order)
    {
        var page = _services.GetRequiredService<ReceivingOrderSkuPage>();
        page.Show(order);
        await Navigation.PushAsync(page);
    }

    private void SetBusy(bool busy)
    {
        Interlocked.Increment(ref _scanGeneration);
        _busy = busy;
        ProgressIndicator.IsVisible = busy;
        CreateButton.IsEnabled = !busy && Warehouse is not null;
        CameraButton.IsEnabled = !busy && Warehouse is not null;
        WarehousePicker.IsEnabled = !busy;
        if (busy) CameraScannerView.Stop();
    }
}

public sealed record ReceivingWorkOrderRow(
    Guid Id, string Number, string CreatedAtText,
    string StatusGlyph, string StatusDescription, Color StatusColor)
{
    public FontAttributes StatusFontAttributes =>
        ReceivingStatusSymbols.FontForGlyph(StatusGlyph);
}

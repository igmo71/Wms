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
    private readonly HashSet<Guid> _selected = [];
    private (Guid RequestId, Guid[] SourceRefKeys)? _pendingCreate;
    private IReadOnlyList<MobileReceivingSourceResponse> _sources = [];
    private bool _loaded;
    private bool _visible;
    private bool _busy;
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

    public IReadOnlyList<ReceivingSourceRow> SourceRows { get; private set; } = [];
    public IReadOnlyList<ReceivingWorkOrderRow> OrderRows { get; private set; } = [];
    private MobileWarehouseResponse? Warehouse => WarehousePicker.SelectedItem as MobileWarehouseResponse;

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _visible = true;
        _scanner.ScanReceived += OnScanReceived;
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
        Interlocked.Increment(ref _scanGeneration);
        _scanner.ScanReceived -= OnScanReceived;
        CameraScannerView.Stop();
        base.OnDisappearing();
    }

    private async void OnWarehouseChanged(object? sender, EventArgs e)
    {
        if (Warehouse is { } warehouse) _selection.Select(warehouse.Id);
        _selected.Clear();
        _pendingCreate = null;
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
            _sources = await _orders.GetUnlinkedSourcesAsync(warehouse.Id);
            var orders = await _orders.GetOrdersAsync(warehouse.Id);
            _selected.IntersectWith(_sources.Select(x => x.RefKey));
            OrderRows = orders.Select(x => new ReceivingWorkOrderRow(
                x.Id, x.Id.ToString(), StatusText(x.Status))).ToArray();
            OnPropertyChanged(nameof(OrderRows));
            ShowSelection();
        }
        catch (MobileApiException ex) { ErrorLabel.Text = ex.Message; }
        catch (HttpRequestException) { ErrorLabel.Text = "Не удалось обновить документы и ордера."; }
        finally { SetBusy(false); }
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
        SetBusy(true);
        ErrorLabel.Text = string.Empty;
        try
        {
            var resolved = await _orders.ResolveDocumentAsync(warehouse.Id, barcode);
            if (resolved.Order is { } order)
            {
                await OpenOrderAsync(order);
                return;
            }
            if (resolved.Source is { } source)
            {
                _pendingCreate = null;
                _selected.Add(source.RefKey);
                _sources = await _orders.GetUnlinkedSourcesAsync(warehouse.Id);
                ShowSelection();
            }
        }
        catch (MobileApiException ex) { ErrorLabel.Text = ex.Message; }
        catch (HttpRequestException) { ErrorLabel.Text = "Сервер WMS недоступен. Повторите сканирование."; }
        finally { SetBusy(false); await UpdateCameraAsync(); }
    }

    private void OnSourceTapped(object? sender, TappedEventArgs e)
    {
        if (_busy || e.Parameter is not Guid id) return;
        _pendingCreate = null;
        if (!_selected.Add(id)) _selected.Remove(id);
        ShowSelection();
    }

    private void ShowSelection()
    {
        SourceRows = _sources.Select(x => new ReceivingSourceRow(
            x.RefKey, x.Number ?? x.RefKey.ToString(),
            _selected.Contains(x.RefKey) ? "☑" : "□",
            x.PlanBySku.Count == 0 ? "План отсутствует"
                : string.Join("; ", x.PlanBySku.Select(p =>
                    $"{p.Key}: {(p.Value?.ToString("0.###") ?? "неизвестно")}")),
            x.LastAttemptError ?? (x.HasUnknownSku ? "Есть неопределённые строки" : string.Empty))).ToArray();
        OnPropertyChanged(nameof(SourceRows));
        var selectedSources = _sources.Where(x => _selected.Contains(x.RefKey)).ToArray();
        var plan = selectedSources.SelectMany(x => x.PlanBySku)
            .GroupBy(x => x.Key).Select(group =>
                $"{group.Key}: {(group.Any(x => x.Value is null) ? "неизвестно" : group.Sum(x => x.Value!.Value).ToString("0.###"))}");
        SelectionLabel.Text = selectedSources.Length == 0 ? "Документы не выбраны."
            : $"Выбрано: {selectedSources.Length}. Суммарный план: {string.Join("; ", plan)}";
        CreateButton.IsEnabled = !_busy && (_pendingCreate is not null || selectedSources.Length > 0);
    }

    private async void OnCreateClicked(object? sender, EventArgs e)
    {
        if (_busy || (_selected.Count == 0 && _pendingCreate is null)) return;
        SetBusy(true);
        ErrorLabel.Text = string.Empty;
        try
        {
            _pendingCreate ??= (Guid.NewGuid(), _selected.ToArray());
            var pending = _pendingCreate.Value;
            var result = await _orders.CreateOrderAsync(
                pending.RequestId, pending.SourceRefKeys);
            await OpenOrderAsync(await _orders.GetAsync(result.ResourceId));
            _pendingCreate = null;
            _selected.Clear();
        }
        catch (MobileApiException ex)
        {
            _pendingCreate = null;
            ErrorLabel.Text = ex.Message;
        }
        catch (HttpRequestException) { ErrorLabel.Text = "Результат создания не подтверждён. Повторите те же документы."; }
        finally { SetBusy(false); await UpdateCameraAsync(); }
    }

    private async void OnOrderTapped(object? sender, TappedEventArgs e)
    {
        if (_busy || e.Parameter is not Guid id) return;
        SetBusy(true);
        try { await OpenOrderAsync(await _orders.GetAsync(id)); }
        catch (MobileApiException ex) { ErrorLabel.Text = ex.Message; }
        catch (HttpRequestException) { ErrorLabel.Text = "Сервер WMS недоступен."; }
        finally { SetBusy(false); await UpdateCameraAsync(); }
    }

    private async Task OpenOrderAsync(MobileReceivingWorkOrderResponse order)
    {
        var page = _services.GetRequiredService<ReceivingOrderReceivingPage>();
        page.Show(order);
        await Navigation.PushAsync(page);
    }

    private void SetBusy(bool busy)
    {
        Interlocked.Increment(ref _scanGeneration);
        _busy = busy;
        ProgressIndicator.IsVisible = busy;
        CreateButton.IsEnabled = !busy && (_pendingCreate is not null || _selected.Count > 0);
    }

    private async Task UpdateCameraAsync()
    {
        if (_visible && !_busy && _scanner.ActiveSource == BarcodeScanSource.Camera)
            await CameraScannerView.StartAsync();
        else CameraScannerView.Stop();
    }

    private static string StatusText(MobileReceivingOrderStatus status) => status switch
    {
        MobileReceivingOrderStatus.ReadyForReceiving => "К приёмке",
        MobileReceivingOrderStatus.InReceiving => "В работе",
        MobileReceivingOrderStatus.Received => "Принят",
        _ => status.ToString()
    };
}

public sealed record ReceivingSourceRow(Guid RefKey, string Number,
    string SelectionMark, string PlanText, string Warning);
public sealed record ReceivingWorkOrderRow(Guid Id, string Number, string StatusText);

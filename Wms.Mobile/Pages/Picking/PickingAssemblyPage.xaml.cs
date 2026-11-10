using Microsoft.Extensions.DependencyInjection;
using Wms.Contracts.Mobile.V1;
using Wms.Mobile.Scanning;
using Wms.Mobile.Services;

namespace Wms.Mobile;

public partial class PickingAssemblyPage : ContentPage
{
    private readonly MobilePickingClient _client;
    private readonly MobileReferenceDataClient _references;
    private readonly MobileWarehouseSelection _selection;
    private readonly IOperationalBarcodeScanner _scanner;
    private readonly PickingAssemblyProcess _process;
    private readonly IServiceProvider _services;
    private bool _loaded;
    private bool _visible;
    private bool _busy;
    private bool _cameraRequested;
    private long _scanGeneration;

    public PickingAssemblyPage(MobilePickingClient client,
        MobileReferenceDataClient references, MobileWarehouseSelection selection,
        IOperationalBarcodeScanner scanner, PickingAssemblyProcess process,
        IServiceProvider services)
    {
        InitializeComponent();
        _client = client;
        _references = references;
        _selection = selection;
        _scanner = scanner;
        _process = process;
        _services = services;
        BindingContext = this;
        CameraScannerView.Configure(scanner);
    }

    public IReadOnlyList<PickingAssemblyQueueRow> AssemblyRows { get; private set; } = [];
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
                var warehouses = await _references.GetWarehousesAsync();
                WarehousePicker.ItemsSource = warehouses.ToList();
                var (index, warning) = _selection.Resolve(warehouses);
                if (index >= 0) WarehousePicker.SelectedIndex = index;
                ErrorLabel.Text = warning ?? string.Empty;
                _loaded = true;
            }
            catch (MobileApiException exception) { ErrorLabel.Text = exception.Message; }
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
            var items = await _client.ListAsync(warehouse.Id);
            AssemblyRows = items.Select(x => new PickingAssemblyQueueRow(
                x.Id, x.Number,
                $"{StatusText(x.Status)} · РО 1С: {x.Sources.Count} · Ответственный: {x.ResponsibleDisplayName}",
                x.HasWarning ? "Требуется внимание к документам 1С" : null)).ToArray();
            OnPropertyChanged(nameof(AssemblyRows));
            ErrorLabel.Text = string.Empty;
        }
        catch (MobileApiException exception) { ErrorLabel.Text = exception.Message; }
        catch (HttpRequestException) { ErrorLabel.Text = "Не удалось обновить список сборок."; }
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
        if (!_visible || _busy || _process.HasPendingCommand
            || Warehouse is not { } warehouse) return;
        SetBusy(true);
        ErrorLabel.Text = string.Empty;
        try
        {
            var resolved = await _client.ResolveAsync(warehouse.Id, barcode);
            if (resolved.Assembly is { } existing)
            {
                await OpenSkuAsync(existing);
                return;
            }
            if (resolved.FreeSource is null)
            {
                ErrorLabel.Text = "РО 1С не найден.";
                return;
            }
            var created = await _process.CreateAsync(warehouse.Id, barcode);
            await OpenSourcesAsync(created);
        }
        catch (MobileApiException exception) { ErrorLabel.Text = exception.Message; }
        catch (HttpRequestException)
        {
            ErrorLabel.Text = _process.HasPendingCommand
                ? "Ответ не подтверждён. Повторите создание той же сборки."
                : "Сервер WMS недоступен. Повторите сканирование.";
        }
        finally { SetBusy(false); await UpdateCameraAsync(); }
    }

    private async void OnAssemblyTapped(object? sender, TappedEventArgs e)
    {
        if (_busy || _process.HasPendingCommand || Warehouse is not { } warehouse
            || e.Parameter is not Guid id) return;
        SetBusy(true);
        try { await OpenSkuAsync(await _client.GetAsync(id, warehouse.Id)); }
        catch (MobileApiException exception) { ErrorLabel.Text = exception.Message; }
        catch (HttpRequestException) { ErrorLabel.Text = "Не удалось открыть сборку."; }
        finally { SetBusy(false); }
    }

    private async void OnCreateClicked(object? sender, EventArgs e)
    {
        if (_busy || _process.HasPendingCommand || Warehouse is not { } warehouse) return;
        var page = _services.GetRequiredService<PickingSourcesPage>();
        page.ShowForCreation(warehouse.Id, _process);
        await Navigation.PushAsync(page);
    }

    private async void OnRetryClicked(object? sender, EventArgs e)
    {
        if (_busy || !_process.HasPendingCommand) return;
        SetBusy(true);
        try { await OpenSourcesAsync(await _process.RetryAsync()); }
        catch (MobileApiException exception) { ErrorLabel.Text = exception.Message; }
        catch (HttpRequestException)
        { ErrorLabel.Text = "Ответ не подтверждён. Повторите ту же команду."; }
        finally { SetBusy(false); await UpdateCameraAsync(); }
    }

    private async Task OpenSkuAsync(MobilePickingAssemblyResponse assembly)
    {
        var page = _services.GetRequiredService<PickingSkuPage>();
        page.Show(assembly, _process);
        await Navigation.PushAsync(page);
    }

    private async Task OpenSourcesAsync(MobilePickingAssemblyResponse assembly)
    {
        var page = _services.GetRequiredService<PickingSourcesPage>();
        page.Show(assembly, _process);
        await Navigation.PushAsync(page);
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
        CameraButton.Text = _cameraRequested ? "Закрыть камеру" : "Сканировать РО 1С";
        if (_visible && !_busy && !_process.HasPendingCommand && _cameraRequested
            && Warehouse is not null && _scanner.ActiveSource == BarcodeScanSource.Camera)
            await CameraScannerView.StartAsync();
        else CameraScannerView.Stop();
    }

    private void SetBusy(bool busy)
    {
        Interlocked.Increment(ref _scanGeneration);
        _busy = busy;
        ProgressIndicator.IsVisible = busy;
        RetryButton.IsVisible = !busy && _process.HasPendingCommand;
        CreateButton.IsEnabled = !busy && !_process.HasPendingCommand && Warehouse is not null;
        WarehousePicker.IsEnabled = !busy && !_process.HasPendingCommand;
        CameraButton.IsEnabled = !busy && !_process.HasPendingCommand && Warehouse is not null;
        NavigationPage.SetHasBackButton(this, !busy && !_process.HasPendingCommand);
        if (busy) CameraScannerView.Stop();
    }

    protected override bool OnBackButtonPressed()
    {
        if (!_busy && !_process.HasPendingCommand) return base.OnBackButtonPressed();
        ErrorLabel.Text = "Сначала завершите повтор команды.";
        return true;
    }

    private static string StatusText(MobilePickingAssemblyStatus status) => status switch
    {
        MobilePickingAssemblyStatus.Draft => "Черновик",
        MobilePickingAssemblyStatus.Preparing => "Подготовка",
        MobilePickingAssemblyStatus.InProgress => "В работе",
        MobilePickingAssemblyStatus.Closed => "Закрыта",
        MobilePickingAssemblyStatus.CancellationPending => "Отмена",
        _ => "Отменена"
    };
}

public sealed record PickingAssemblyQueueRow(
    Guid Id, string Number, string DetailText, string? WarningText)
{
    public bool HasWarning => WarningText is not null;
}

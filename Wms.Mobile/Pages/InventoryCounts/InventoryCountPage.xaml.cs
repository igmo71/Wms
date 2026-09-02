using Wms.Contracts.Mobile.V1;
using Wms.Mobile.Scanning;
using Wms.Mobile.Services;

namespace Wms.Mobile;

public partial class InventoryCountPage : ContentPage
{
    private readonly MobileApiClient _apiClient;
    private readonly IOperationalBarcodeScanner _scanner;
    private bool _loaded;
    private bool _isVisible;
    private bool _scannerSubscribed;
    private bool _busy;
    private int _draftLoadVersion;
    private Guid? _pendingStartRequestId;
    private string? _pendingBarcode;

    public InventoryCountPage(MobileApiClient apiClient, IOperationalBarcodeScanner scanner)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _scanner = scanner;
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

        if (!_loaded)
        {
            await LoadWarehousesAsync();
        }
        else
        {
            await LoadDraftsAsync();
        }
        await UpdateCameraAsync();
    }

    protected override void OnDisappearing()
    {
        _isVisible = false;
        _draftLoadVersion++;
        CameraScannerView.Stop();
        if (_scannerSubscribed)
        {
            _scanner.ScanReceived -= OnScanReceived;
            _scannerSubscribed = false;
        }
        base.OnDisappearing();
    }

    private MobileWarehouseResponse? SelectedWarehouse =>
        WarehousePicker.SelectedItem as MobileWarehouseResponse;

    private async Task LoadWarehousesAsync()
    {
        SetBusy(true);
        try
        {
            var warehouses = await _apiClient.GetWarehousesAsync();
            WarehousePicker.ItemsSource = warehouses.ToList();
            _loaded = true;
            if (warehouses.Count == 1)
                WarehousePicker.SelectedIndex = 0;
        }
        catch (MobileApiException exception)
        {
            ErrorLabel.Text = exception.Message;
        }
        catch (HttpRequestException)
        {
            ErrorLabel.Text = "Сервер WMS недоступен.";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void OnWarehouseChanged(object? sender, EventArgs e)
    {
        if (_pendingStartRequestId is not null)
            return;

        ErrorLabel.Text = string.Empty;
        _pendingBarcode = null;
        StepLabel.Text = SelectedWarehouse is null ? "Выберите склад" : "Ячейка";
        InstructionLabel.Text = SelectedWarehouse is null
            ? "После выбора склада отсканируйте QR ячейки хранения."
            : "Отсканируйте QR ячейки. Свободная ячейка будет заблокирована для пересчёта.";
        await LoadDraftsAsync();
        await UpdateCameraAsync();
    }

    private void OnScanReceived(object? sender, BarcodeScanEvent scanEvent) =>
        MainThread.BeginInvokeOnMainThread(async () => await StartAsync(scanEvent.Value));

    private async Task StartAsync(string barcode)
    {
        if (_busy || SelectedWarehouse is not MobileWarehouseResponse warehouse)
            return;
        if (_pendingBarcode is not null && _pendingBarcode != barcode)
        {
            ErrorLabel.Text = "Повторите сканирование предыдущей ячейки: ответ сервера не был получен.";
            return;
        }

        _pendingBarcode ??= barcode;
        _pendingStartRequestId ??= Guid.NewGuid();
        SetBusy(true);
        ErrorLabel.Text = string.Empty;
        try
        {
            var details = await _apiClient.StartInventoryCountAsync(
                warehouse.Id,
                barcode,
                _pendingStartRequestId.Value);
            _pendingStartRequestId = null;
            _pendingBarcode = null;
            if (_isVisible)
                await Navigation.PushAsync(new InventoryCountDetailsPage(_apiClient, _scanner, details));
        }
        catch (MobileApiException exception)
        {
            _pendingStartRequestId = null;
            _pendingBarcode = null;
            ErrorLabel.Text = exception.Message;
        }
        catch (HttpRequestException)
        {
            ErrorLabel.Text = "Ответ сервера не получен. Повторно отсканируйте эту же ячейку.";
        }
        finally
        {
            SetBusy(false);
            await UpdateCameraAsync();
        }
    }

    private async Task LoadDraftsAsync()
    {
        var loadVersion = ++_draftLoadVersion;
        DraftsContainer.Children.Clear();
        if (SelectedWarehouse is not MobileWarehouseResponse warehouse)
        {
            DraftsStatusLabel.Text = "Выберите склад.";
            return;
        }

        try
        {
            var drafts = await _apiClient.GetInventoryCountDraftsAsync(warehouse.Id);
            if (loadVersion != _draftLoadVersion || SelectedWarehouse?.Id != warehouse.Id)
                return;

            DraftsStatusLabel.Text = drafts.Count == 0
                ? "Незавершённых инвентаризаций нет."
                : $"В работе: {drafts.Count}.";
            foreach (var draft in drafts)
                DraftsContainer.Children.Add(CreateDraftCard(draft));
        }
        catch (MobileApiException exception)
        {
            if (loadVersion == _draftLoadVersion)
                DraftsStatusLabel.Text = exception.Message;
        }
        catch (HttpRequestException)
        {
            if (loadVersion == _draftLoadVersion)
                DraftsStatusLabel.Text = "Не удалось загрузить черновики.";
        }
    }

    private View CreateDraftCard(MobileInventoryCountSummaryResponse draft)
    {
        var remaining = draft.TotalItems - draft.CountedItems;
        var layout = new VerticalStackLayout { Spacing = 4 };
        layout.Children.Add(new Label
        {
            Text = $"Инвентаризация {draft.Number}",
            FontAttributes = FontAttributes.Bold,
            FontSize = 18
        });
        layout.Children.Add(new Label
        {
            Text = $"{draft.StorageLocation.Address} · {draft.StorageLocation.Name}\n"
                + $"Пересчитано: {draft.CountedItems} из {draft.TotalItems} · Осталось: {remaining}",
            FontSize = 16
        });
        var border = new Border { Padding = 14, Content = layout };
        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) => await OpenDraftAsync(draft.Id);
        border.GestureRecognizers.Add(tap);
        return border;
    }

    private async Task OpenDraftAsync(Guid inventoryCountId)
    {
        if (_busy || _pendingStartRequestId is not null)
        {
            if (_pendingStartRequestId is not null)
                ErrorLabel.Text = "Сначала повторите сканирование предыдущей ячейки.";
            return;
        }
        SetBusy(true);
        try
        {
            var details = await _apiClient.GetInventoryCountAsync(inventoryCountId);
            if (_isVisible)
                await Navigation.PushAsync(new InventoryCountDetailsPage(_apiClient, _scanner, details));
        }
        catch (MobileApiException exception)
        {
            ErrorLabel.Text = exception.Message;
        }
        catch (HttpRequestException)
        {
            ErrorLabel.Text = "Сервер WMS недоступен.";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task UpdateCameraAsync()
    {
        if (_isVisible
            && _scanner.ActiveSource == BarcodeScanSource.Camera
            && !_busy
            && SelectedWarehouse is not null)
            await CameraScannerView.StartAsync();
        else
            CameraScannerView.Stop();
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        ProgressIndicator.Opacity = busy ? 1 : 0;
        WarehousePicker.IsEnabled = !busy && _pendingStartRequestId is null;
    }
}

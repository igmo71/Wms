using Wms.Contracts.Mobile.V1;
using Wms.Mobile.Scanning;
using Wms.Mobile.Services;

namespace Wms.Mobile;

public partial class ReceivingOrderPage : ContentPage
{
    private readonly MobileReceivingOrderClient _orderClient;
    private readonly MobileReferenceDataClient _referenceDataClient;
    private readonly IOperationalBarcodeScanner _scanner;
    private readonly IServiceProvider _services;
    private bool _loaded;
    private bool _isVisible;
    private bool _scannerSubscribed;
    private bool _busy;
    private bool _showPutaway;
    private Guid? _pendingJoinOrderId;
    private Guid? _pendingJoinRequestId;
    private int _loadVersion;

    public ReceivingOrderPage(
        MobileReceivingOrderClient orderClient,
        MobileReferenceDataClient referenceDataClient,
        IOperationalBarcodeScanner scanner,
        IServiceProvider services)
    {
        InitializeComponent();
        _orderClient = orderClient;
        _referenceDataClient = referenceDataClient;
        _scanner = scanner;
        _services = services;
        BindingContext = this;
        CameraScannerView.Configure(scanner);
    }

    public IReadOnlyList<ReceivingOrderQueueItemViewState> ReceivingOrders { get; private set; } = [];
    public IReadOnlyList<ReceivingOrderQueueItemViewState> PutawayOrders { get; private set; } = [];

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
            await LoadQueueAsync();
        }

        await UpdateCameraAsync();
    }

    protected override void OnDisappearing()
    {
        _isVisible = false;
        _loadVersion++;
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
            var warehouses = await _referenceDataClient.GetWarehousesAsync();
            WarehousePicker.ItemsSource = warehouses.ToList();
            _loaded = true;
            if (warehouses.Count == 1)
            {
                WarehousePicker.SelectedIndex = 0;
            }
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
        ErrorLabel.Text = string.Empty;
        ScanStepLabel.Text = SelectedWarehouse is null
            ? "Выберите склад"
            : "Приходный ордер";
        ScanInstructionLabel.Text = SelectedWarehouse is null
            ? "После выбора склада отсканируйте штрихкод приходного ордера."
            : "Отсканируйте штрихкод приходного ордера или выберите его в очереди ниже.";
        await LoadQueueAsync();
        await UpdateCameraAsync();
    }

    private async void OnRefreshing(object? sender, EventArgs e)
    {
        await LoadQueueAsync();
        QueueRefreshView.IsRefreshing = false;
    }

    private void OnReceivingTabClicked(object? sender, EventArgs e) => ShowPutaway(false);

    private void OnPutawayTabClicked(object? sender, EventArgs e) => ShowPutaway(true);

    private void ShowPutaway(bool showPutaway)
    {
        _showPutaway = showPutaway;
        ReceivingSection.IsVisible = !showPutaway;
        PutawaySection.IsVisible = showPutaway;
        ReceivingTabButton.Opacity = showPutaway ? 0.65 : 1;
        PutawayTabButton.Opacity = showPutaway ? 1 : 0.65;
    }

    private void OnScanReceived(object? sender, BarcodeScanEvent scanEvent) =>
        MainThread.BeginInvokeOnMainThread(async () => await ResolveDocumentAsync(scanEvent.Value));

    private async Task ResolveDocumentAsync(string barcode)
    {
        if (_busy || SelectedWarehouse is not MobileWarehouseResponse warehouse)
        {
            return;
        }

        SetBusy(true);
        ErrorLabel.Text = string.Empty;
        try
        {
            var details = await _orderClient.ResolveDocumentAsync(
                warehouse.Id,
                barcode);
            await OpenDetailsAsync(details);
        }
        catch (MobileApiException exception)
        {
            ErrorLabel.Text = exception.Message;
        }
        catch (HttpRequestException)
        {
            ErrorLabel.Text = "Сервер WMS недоступен. Очередь остаётся доступной.";
        }
        finally
        {
            SetBusy(false);
            await UpdateCameraAsync();
        }
    }

    private async void OnOrderTapped(object? sender, TappedEventArgs e)
    {
        if (_busy || e.Parameter is not Guid orderId)
        {
            return;
        }

        SetBusy(true);
        ErrorLabel.Text = string.Empty;
        try
        {
            var details = await _orderClient.GetAsync(orderId);
            await OpenDetailsAsync(details);
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
            await UpdateCameraAsync();
        }
    }

    private async Task OpenDetailsAsync(MobileReceivingOrderDetailsResponse details)
    {
        if (!_isVisible)
        {
            return;
        }

        if (!details.Order.IsParticipant)
        {
            if (!details.Order.CanJoin)
            {
                ErrorLabel.Text = details.Order.JoinBlockedReason
                    ?? "Нельзя присоединиться к этому ордеру.";
                return;
            }

            if (details.Order.Status == MobileReceivingOrderStatus.InReceiving)
            {
                _pendingJoinOrderId = details.Order.Id;
                _pendingJoinRequestId ??= Guid.NewGuid();
                try
                {
                    var response = await _orderClient.JoinAsync(
                        details.Order.Id,
                        null,
                        _pendingJoinRequestId.Value);
                    _pendingJoinOrderId = null;
                    _pendingJoinRequestId = null;
                    details = response.Details;
                }
                catch (MobileApiException)
                {
                    _pendingJoinOrderId = null;
                    _pendingJoinRequestId = null;
                    throw;
                }
            }
        }

        var receivingPage = _services.GetRequiredService<ReceivingOrderReceivingPage>();
        receivingPage.Show(details);
        await Navigation.PushAsync(receivingPage);
    }

    private async Task LoadQueueAsync()
    {
        var loadVersion = ++_loadVersion;
        if (SelectedWarehouse is not MobileWarehouseResponse warehouse)
        {
            ApplyQueue([], []);
            ReceivingStatusLabel.Text = "Выберите склад.";
            PutawayStatusLabel.Text = "Выберите склад.";
            return;
        }

        try
        {
            var queue = await _orderClient.GetWorkQueueAsync(warehouse.Id);
            if (loadVersion != _loadVersion || SelectedWarehouse?.Id != warehouse.Id)
            {
                return;
            }

            ApplyQueue(
                queue.Personal.Select(ReceivingOrderQueueItemViewState.ForReceiving).ToList(),
                queue.Available.Select(ReceivingOrderQueueItemViewState.ForReceiving).ToList());
            ReceivingStatusLabel.Text = queue.Personal.Count == 0
                ? "Личных ордеров в работе нет."
                : $"Личных ордеров: {queue.Personal.Count}.";
            PutawayStatusLabel.Text = queue.Available.Count == 0
                ? "Доступных ордеров нет."
                : $"Доступных ордеров: {queue.Available.Count}.";
        }
        catch (MobileApiException exception)
        {
            if (loadVersion == _loadVersion)
            {
                ReceivingStatusLabel.Text = exception.Message;
                PutawayStatusLabel.Text = exception.Message;
            }
        }
        catch (HttpRequestException)
        {
            if (loadVersion == _loadVersion)
            {
                ReceivingStatusLabel.Text = "Не удалось обновить очередь.";
                PutawayStatusLabel.Text = "Не удалось обновить очередь.";
            }
        }
    }

    private void ApplyQueue(
        IReadOnlyList<ReceivingOrderQueueItemViewState> receiving,
        IReadOnlyList<ReceivingOrderQueueItemViewState> putaway)
    {
        ReceivingOrders = receiving;
        PutawayOrders = putaway;
        OnPropertyChanged(nameof(ReceivingOrders));
        OnPropertyChanged(nameof(PutawayOrders));
        ShowPutaway(_showPutaway);
    }

    private async Task UpdateCameraAsync()
    {
        if (_isVisible
            && _scanner.ActiveSource == BarcodeScanSource.Camera
            && !_busy
            && SelectedWarehouse is not null)
        {
            await CameraScannerView.StartAsync();
        }
        else
        {
            CameraScannerView.Stop();
        }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        ProgressIndicator.Opacity = busy ? 1 : 0;
        WarehousePicker.IsEnabled = !busy;
        ReceivingTabButton.IsEnabled = !busy;
        PutawayTabButton.IsEnabled = !busy;
    }

    private void OnNonScanControlLoaded(object? sender, EventArgs e)
    {
        if (sender is VisualElement element)
        {
            AndroidFocus.Suppress(element);
        }
    }
}

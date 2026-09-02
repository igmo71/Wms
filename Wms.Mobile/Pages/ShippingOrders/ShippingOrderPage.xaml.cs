using Wms.Contracts.Mobile.V1;
using Wms.Mobile.Scanning;
using Wms.Mobile.Services;

namespace Wms.Mobile;

public partial class ShippingOrderPage : ContentPage
{
    private readonly MobileApiClient _apiClient;
    private readonly IOperationalBarcodeScanner _scanner;
    private readonly IServiceProvider _services;
    private bool _loaded;
    private bool _isVisible;
    private bool _scannerSubscribed;
    private bool _busy;
    private bool _showShipping;
    private int _loadVersion;

    public ShippingOrderPage(
        MobileApiClient apiClient,
        IOperationalBarcodeScanner scanner,
        IServiceProvider services)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _scanner = scanner;
        _services = services;
        BindingContext = this;
        CameraScannerView.Configure(scanner);
    }

    public IReadOnlyList<ShippingOrderQueueItemViewState> PickingOrders { get; private set; } = [];
    public IReadOnlyList<ShippingOrderQueueItemViewState> ShippingOrders { get; private set; } = [];

    private MobileWarehouseResponse? SelectedWarehouse =>
        WarehousePicker.SelectedItem as MobileWarehouseResponse;

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

    private async Task LoadWarehousesAsync()
    {
        SetBusy(true);
        try
        {
            var warehouses = await _apiClient.GetWarehousesAsync();
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
            : "Расходный ордер";
        ScanInstructionLabel.Text = SelectedWarehouse is null
            ? "После выбора склада отсканируйте штрихкод расходного ордера."
            : "Отсканируйте штрихкод расходного ордера или выберите его в очереди ниже.";
        await LoadQueueAsync();
        await UpdateCameraAsync();
    }

    private async void OnRefreshing(object? sender, EventArgs e)
    {
        await LoadQueueAsync();
        QueueRefreshView.IsRefreshing = false;
    }

    private void OnPickingTabClicked(object? sender, EventArgs e) => ShowShipping(false);

    private void OnShippingTabClicked(object? sender, EventArgs e) => ShowShipping(true);

    private void ShowShipping(bool showShipping)
    {
        _showShipping = showShipping;
        PickingSection.IsVisible = !showShipping;
        ShippingSection.IsVisible = showShipping;
        PickingTabButton.Opacity = showShipping ? 0.65 : 1;
        ShippingTabButton.Opacity = showShipping ? 1 : 0.65;
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
            var details = await _apiClient.ResolveShippingOrderDocumentAsync(warehouse.Id, barcode);
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
            var details = await _apiClient.GetShippingOrderAsync(orderId);
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

    private async Task OpenDetailsAsync(MobileShippingOrderDetailsResponse details)
    {
        if (!_isVisible)
        {
            return;
        }

        if (details.Order.Status == MobileShippingOrderStatus.ReadyForShipment)
        {
            var shippingPage = _services.GetRequiredService<ShippingOrderShippingPage>();
            shippingPage.Show(details);
            await Navigation.PushAsync(shippingPage);
            return;
        }

        var pickingPage = _services.GetRequiredService<ShippingOrderPickingPage>();
        pickingPage.Show(details);
        await Navigation.PushAsync(pickingPage);
    }

    private async Task LoadQueueAsync()
    {
        var loadVersion = ++_loadVersion;
        if (SelectedWarehouse is not MobileWarehouseResponse warehouse)
        {
            ApplyQueue([], []);
            PickingStatusLabel.Text = "Выберите склад.";
            ShippingStatusLabel.Text = "Выберите склад.";
            return;
        }

        try
        {
            var queue = await _apiClient.GetShippingOrderWorkQueueAsync(warehouse.Id);
            if (loadVersion != _loadVersion || SelectedWarehouse?.Id != warehouse.Id)
            {
                return;
            }

            ApplyQueue(
                queue.Picking.Select(ShippingOrderQueueItemViewState.ForPicking).ToList(),
                queue.Shipping.Select(ShippingOrderQueueItemViewState.ForShipping).ToList());
            PickingStatusLabel.Text = queue.Picking.Count == 0
                ? "Ордера для отбора отсутствуют."
                : $"Ордеров: {queue.Picking.Count}.";
            ShippingStatusLabel.Text = queue.Shipping.Count == 0
                ? "Ордера для отгрузки отсутствуют."
                : $"Ордеров: {queue.Shipping.Count}.";
        }
        catch (MobileApiException exception)
        {
            if (loadVersion == _loadVersion)
            {
                PickingStatusLabel.Text = exception.Message;
                ShippingStatusLabel.Text = exception.Message;
            }
        }
        catch (HttpRequestException)
        {
            if (loadVersion == _loadVersion)
            {
                PickingStatusLabel.Text = "Не удалось обновить очередь.";
                ShippingStatusLabel.Text = "Не удалось обновить очередь.";
            }
        }
    }

    private void ApplyQueue(
        IReadOnlyList<ShippingOrderQueueItemViewState> picking,
        IReadOnlyList<ShippingOrderQueueItemViewState> shipping)
    {
        PickingOrders = picking;
        ShippingOrders = shipping;
        OnPropertyChanged(nameof(PickingOrders));
        OnPropertyChanged(nameof(ShippingOrders));
        ShowShipping(_showShipping);
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
        PickingTabButton.IsEnabled = !busy;
        ShippingTabButton.IsEnabled = !busy;
    }

    private void OnNonScanControlLoaded(object? sender, EventArgs e)
    {
        if (sender is VisualElement element)
        {
            DisableAndroidFocus(element);
        }
    }

    private static void DisableAndroidFocus(VisualElement element)
    {
#if ANDROID
        if (element.Handler?.PlatformView is Android.Views.View view)
        {
            view.Focusable = false;
            view.FocusableInTouchMode = false;
        }
#endif
    }
}

using Wms.Contracts.Mobile.V1;
using Wms.Mobile.Services;

namespace Wms.Mobile;

public partial class InventoryTransferPage : ContentPage
{
    private readonly MobileApiClient _apiClient;
    private bool _loaded;
    private Guid? _pendingCreateRequestId;
    private Guid? _pendingCreateWarehouseId;

    public InventoryTransferPage(MobileApiClient apiClient)
    {
        InitializeComponent();
        _apiClient = apiClient;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        await LoadWarehousesAsync();
    }

    private async Task LoadWarehousesAsync()
    {
        SetBusy(true);
        ErrorLabel.Text = string.Empty;

        try
        {
            var warehouses = await _apiClient.GetWarehousesAsync();
            WarehousePicker.ItemsSource = warehouses.ToList();
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
        await LoadTransfersAsync();
    }

    private async void OnRefreshClicked(object? sender, EventArgs e)
    {
        await LoadTransfersAsync();
    }

    private async void OnCreateClicked(object? sender, EventArgs e)
    {
        if (WarehousePicker.SelectedItem is not MobileWarehouseResponse selectedWarehouse)
        {
            ErrorLabel.Text = "Выберите склад.";
            return;
        }

        var warehouseId = _pendingCreateWarehouseId ?? selectedWarehouse.Id;
        _pendingCreateRequestId ??= Guid.NewGuid();
        _pendingCreateWarehouseId ??= warehouseId;

        SetBusy(true);
        ErrorLabel.Text = string.Empty;

        try
        {
            await _apiClient.CreateInventoryTransferAsync(
                warehouseId,
                _pendingCreateRequestId.Value);
            ClearPendingCreate();
            await LoadTransfersAsync();
        }
        catch (MobileApiException exception)
        {
            ClearPendingCreate();
            ErrorLabel.Text = exception.Message;
        }
        catch (HttpRequestException)
        {
            ErrorLabel.Text =
                "Ответ сервера не получен. Нажмите «Повторить создание».";
            NewTransferButton.Text = "Повторить создание";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task LoadTransfersAsync()
    {
        if (WarehousePicker.SelectedItem is not MobileWarehouseResponse warehouse)
        {
            TransfersView.ItemsSource = null;
            return;
        }

        SetBusy(true);
        ErrorLabel.Text = string.Empty;

        try
        {
            var transfers = await _apiClient.GetInventoryTransfersAsync(warehouse.Id);
            TransfersView.ItemsSource = transfers.Select(x => new TransferListItem(
                x.Number,
                x.Date.ToString("dd.MM.yyyy"),
                GetStatusText(x.Status))).ToList();
        }
        catch (MobileApiException exception)
        {
            ErrorLabel.Text = exception.Message;
            TransfersView.ItemsSource = null;
        }
        catch (HttpRequestException)
        {
            ErrorLabel.Text = "Сервер WMS недоступен.";
            TransfersView.ItemsSource = null;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool isBusy)
    {
        ProgressIndicator.IsVisible = isBusy;
        ProgressIndicator.IsRunning = isBusy;
        WarehousePicker.IsEnabled = !isBusy && _pendingCreateRequestId is null;
        RefreshButton.IsEnabled = !isBusy;
        NewTransferButton.IsEnabled = !isBusy;
    }

    private void ClearPendingCreate()
    {
        _pendingCreateRequestId = null;
        _pendingCreateWarehouseId = null;
        NewTransferButton.Text = "Новое перемещение";
    }

    private static string GetStatusText(MobileInventoryTransferStatus status) => status switch
    {
        MobileInventoryTransferStatus.Draft => "Черновик",
        MobileInventoryTransferStatus.InProgress => "В работе",
        MobileInventoryTransferStatus.Completed => "Завершено",
        _ => status.ToString()
    };

    private sealed record TransferListItem(string Number, string Date, string Status);
}

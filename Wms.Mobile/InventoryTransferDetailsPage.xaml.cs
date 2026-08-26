using Wms.Contracts.Mobile.V1;
using Wms.Mobile.Scanning;
using Wms.Mobile.Services;

namespace Wms.Mobile;

public partial class InventoryTransferDetailsPage : ContentPage
{
    private readonly MobileApiClient _apiClient;
    private readonly ILifecycleBarcodeScanner _intentScanner;
    private MobileInventoryTransferSummaryResponse _transfer;
    private Guid? _highlightMovementId;
    private Guid? _pendingCompleteRequestId;
    private bool _busy;
    private bool _detailsLoaded;

    public InventoryTransferDetailsPage(
        MobileApiClient apiClient,
        ILifecycleBarcodeScanner intentScanner,
        MobileInventoryTransferSummaryResponse transfer)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _intentScanner = intentScanner;
        _transfer = transfer;
        ShowTransferHeader();
        SetAvailableActions();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        _detailsLoaded = false;
        SetBusy(true);
        ErrorLabel.Text = string.Empty;

        try
        {
            var details = await _apiClient.GetInventoryTransferAsync(_transfer.Id);
            _transfer = details.Transfer;
            _detailsLoaded = true;
            ShowTransferHeader();

            var items = details.Movements
                .Select(MapMovement)
                .ToList();
            BindableLayout.SetItemsSource(MovementsLayout, items);
            EmptyHistoryLabel.IsVisible = items.Count == 0;
            MovementsTitleLabel.Text = $"История движений · {items.Count}";
        }
        catch (MobileApiException exception)
        {
            ErrorLabel.Text = exception.Message;
            BindableLayout.SetItemsSource(MovementsLayout, null);
            EmptyHistoryLabel.IsVisible = false;
        }
        catch (HttpRequestException)
        {
            ErrorLabel.Text = "Сервер WMS недоступен.";
            BindableLayout.SetItemsSource(MovementsLayout, null);
            EmptyHistoryLabel.IsVisible = false;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void OnAddMovementClicked(object? sender, EventArgs e)
    {
        if (_busy || _transfer.Status == MobileInventoryTransferStatus.Completed)
        {
            return;
        }

        _highlightMovementId = null;
        await Navigation.PushAsync(new DirectInventoryTransferPage(
            _apiClient,
            _intentScanner,
            _transfer,
            OnMovementCompleted));
    }

    private void OnMovementCompleted(MobileMoveDirectInventoryTransferResponse movement)
    {
        _highlightMovementId = movement.MovementId;
        _transfer = _transfer with { Status = movement.TransferStatus };
    }

    private async void OnCompleteTransferClicked(object? sender, EventArgs e)
    {
        if (_busy || _transfer.Status != MobileInventoryTransferStatus.InProgress)
        {
            return;
        }

        if (_pendingCompleteRequestId is null)
        {
            var confirmed = await DisplayAlertAsync(
                "Завершить документ?",
                $"Перемещение {_transfer.Number} больше нельзя будет изменить.",
                "Завершить",
                "Отмена");
            if (!confirmed)
            {
                return;
            }

            _pendingCompleteRequestId = Guid.NewGuid();
        }

        SetBusy(true);
        ErrorLabel.Text = string.Empty;

        try
        {
            await _apiClient.CompleteInventoryTransferAsync(
                _transfer.Id,
                _pendingCompleteRequestId.Value);
            _pendingCompleteRequestId = null;
            await DisplayAlertAsync(
                "Документ завершён",
                $"Перемещение {_transfer.Number} успешно завершено.",
                "К списку");
            await Navigation.PopAsync();
        }
        catch (MobileApiException exception)
        {
            _pendingCompleteRequestId = null;
            CompleteTransferButton.Text = "Завершить";
            ErrorLabel.Text = exception.Message;
        }
        catch (HttpRequestException)
        {
            ErrorLabel.Text =
                "Ответ сервера не получен. Нажмите «Повторить».";
            CompleteTransferButton.Text = "Повторить";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void OnRefreshClicked(object? sender, EventArgs e)
    {
        await LoadAsync();
    }

    private void SetBusy(bool isBusy)
    {
        _busy = isBusy;
        ProgressIndicator.IsVisible = isBusy;
        ProgressIndicator.IsRunning = isBusy;
        SetAvailableActions();
    }

    private void SetAvailableActions()
    {
        AddMovementButton.IsEnabled = !_busy
            && _detailsLoaded
            && _transfer.Status != MobileInventoryTransferStatus.Completed;
        CompleteTransferButton.IsEnabled = !_busy
            && _detailsLoaded
            && _transfer.Status == MobileInventoryTransferStatus.InProgress;
    }

    private void ShowTransferHeader()
    {
        TransferNumberLabel.Text = $"Перемещение {_transfer.Number}";
        TransferContextLabel.Text = $"Статус: {GetStatusText(_transfer.Status)}";
        SetAvailableActions();
    }

    private MovementListItem MapMovement(MobileInventoryTransferMovementResponse movement)
    {
        var unit = string.IsNullOrWhiteSpace(movement.UnitOfMeasure)
            ? string.Empty
            : $" {movement.UnitOfMeasure}";
        var highlighted = movement.MovementId == _highlightMovementId;
        return new MovementListItem(
            movement.MovementId,
            movement.SkuName,
            $"{movement.Quantity:0.###}{unit}",
            $"{movement.Source.Address} → {movement.Destination.Address}",
            highlighted ? Colors.Green : Colors.Gray,
            highlighted ? 3 : 1);
    }

    private void OnActionButtonLoaded(object? sender, EventArgs e)
    {
#if ANDROID
        if (sender is Button { Handler.PlatformView: Android.Widget.Button button })
        {
            button.Focusable = false;
            button.FocusableInTouchMode = false;
        }
#endif
    }

    private static string GetStatusText(MobileInventoryTransferStatus status) => status switch
    {
        MobileInventoryTransferStatus.Draft => "Черновик",
        MobileInventoryTransferStatus.InProgress => "В работе",
        MobileInventoryTransferStatus.Completed => "Завершено",
        _ => status.ToString()
    };

    private sealed record MovementListItem(
        Guid MovementId,
        string SkuName,
        string Quantity,
        string Route,
        Color BorderColor,
        double BorderThickness);
}

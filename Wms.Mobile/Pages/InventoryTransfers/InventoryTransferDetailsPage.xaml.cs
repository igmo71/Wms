using Wms.Contracts.Mobile.V1;
using Wms.Mobile.Scanning;
using Wms.Mobile.Services;

namespace Wms.Mobile;

public partial class InventoryTransferDetailsPage : ContentPage
{
    private readonly MobileInventoryTransferClient _transferClient;
    private readonly MobileReferenceDataClient _referenceDataClient;
    private readonly IOperationalBarcodeScanner _scanner;
    private MobileInventoryTransferSummaryResponse _transfer;
    private IReadOnlyList<MobileInventoryTransferSkuBalanceResponse> _transitBalances = [];
    private Guid? _highlightMovementId;
    private Guid? _pendingCompleteRequestId;
    private bool _busy;
    private bool _detailsLoaded;

    private bool CanMove => !_busy && _detailsLoaded
        && _pendingCompleteRequestId is null
        && _transfer.Status != MobileInventoryTransferStatus.Completed;

    private bool CanComplete => !_busy && _detailsLoaded
        && _transfer.Status == MobileInventoryTransferStatus.InProgress
        && _transitBalances.Count == 0;

    public InventoryTransferDetailsPage(
        MobileInventoryTransferClient transferClient,
        MobileReferenceDataClient referenceDataClient,
        IOperationalBarcodeScanner scanner,
        MobileInventoryTransferSummaryResponse transfer)
    {
        InitializeComponent();
        _transferClient = transferClient;
        _referenceDataClient = referenceDataClient;
        _scanner = scanner;
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
        if (_busy)
            return;

        _detailsLoaded = false;
        SetBusy(true);
        ErrorLabel.Text = string.Empty;

        try
        {
            var details = await _transferClient.GetAsync(_transfer.Id);
            _transfer = details.Transfer;
            _transitBalances = details.TransitBalances;
            _detailsLoaded = true;
            ShowTransferHeader();
            ShowTransitBalances();

            var items = details.Movements.Select(MapMovement).ToList();
            BindableLayout.SetItemsSource(MovementsLayout, items);
            EmptyHistoryLabel.IsVisible = items.Count == 0;
            MovementsTitleLabel.Text = $"История движений · {items.Count}";
        }
        catch (MobileApiException exception)
        {
            ErrorLabel.Text = exception.Message;
            ClearDetails();
        }
        catch (HttpRequestException)
        {
            ErrorLabel.Text = "Сервер WMS недоступен.";
            ClearDetails();
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void ClearDetails()
    {
        _transitBalances = [];
        TransitBalancesLayout.Children.Clear();
        BindableLayout.SetItemsSource(MovementsLayout, null);
        EmptyTransitContentsLabel.IsVisible = false;
        EmptyHistoryLabel.IsVisible = false;
    }

    private async void OnAddMovementClicked(object? sender, EventArgs e)
    {
        if (_transfer.TransitStorageLocation is null)
        {
            await OpenDirectMovementAsync();
        }
        else
        {
            await OpenTransitMovementAsync(TransitInventoryTransferMovementMode.Pick);
        }
    }

    private async void OnPutFromTransitClicked(object? sender, EventArgs e) =>
        await OpenTransitMovementAsync(TransitInventoryTransferMovementMode.Put);

    private async Task OpenDirectMovementAsync()
    {
        if (!CanMove || _transfer.TransitStorageLocation is not null)
        {
            return;
        }

        _highlightMovementId = null;
        await Navigation.PushAsync(new DirectInventoryTransferPage(
            _transferClient,
            _referenceDataClient,
            _scanner,
            _transfer,
            OnMovementCompleted));
    }

    private async Task OpenTransitMovementAsync(
        TransitInventoryTransferMovementMode mode,
        MobileInventoryTransferSkuBalanceResponse? selectedSku = null)
    {
        if (!CanMove
            || _transfer.TransitStorageLocation is null
            || (mode == TransitInventoryTransferMovementMode.Put && _transitBalances.Count == 0))
        {
            return;
        }

        _highlightMovementId = null;
        await Navigation.PushAsync(new TransitInventoryTransferMovementPage(
            _transferClient,
            _referenceDataClient,
            _scanner,
            _transfer,
            mode,
            _transitBalances,
            selectedSku,
            OnTransitMovementCompleted));
    }

    private void OnMovementCompleted(MobileMoveDirectInventoryTransferResponse movement) =>
        RememberMovement(movement.MovementId, movement.TransferStatus);

    private void OnTransitMovementCompleted(
        MobileTransitInventoryTransferMovementResponse movement) =>
        RememberMovement(movement.MovementId, movement.TransferStatus);

    private void RememberMovement(
        Guid movementId,
        MobileInventoryTransferStatus transferStatus)
    {
        _highlightMovementId = movementId;
        _transfer = _transfer with { Status = transferStatus };
    }

    private async void OnCompleteTransferClicked(object? sender, EventArgs e)
    {
        if (!CanComplete)
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
            await _transferClient.CompleteAsync(
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
            ErrorLabel.Text = "Ответ сервера не получен. Нажмите «Повторить».";
            CompleteTransferButton.Text = "Повторить";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void OnRefreshClicked(object? sender, EventArgs e) => await LoadAsync();

    private void SetBusy(bool isBusy)
    {
        _busy = isBusy;
        ProgressIndicator.IsVisible = isBusy;
        ProgressIndicator.IsRunning = isBusy;
        SetAvailableActions();
    }

    private void SetAvailableActions()
    {
        AddMovementButton.IsEnabled = CanMove;
        PutFromTransitButton.IsEnabled = CanMove && _transitBalances.Count > 0;
        CompleteTransferButton.IsEnabled = CanComplete;
    }

    private void ShowTransferHeader()
    {
        TransferNumberLabel.Text = $"Перемещение {_transfer.Number}";
        TransferContextLabel.Text = $"Статус: {GetStatusText(_transfer.Status)}";

        var hasTransit = _transfer.TransitStorageLocation is not null;
        TransitLocationCard.IsVisible = hasTransit;
        TransitContentsSection.IsVisible = hasTransit;
        PutFromTransitButton.IsVisible = hasTransit;
        AddMovementButton.Text = hasTransit ? "В транзит" : "Переместить";
        Grid.SetRow(CompleteTransferButton, hasTransit ? 1 : 0);
        Grid.SetColumn(CompleteTransferButton, hasTransit ? 0 : 1);
        Grid.SetColumnSpan(CompleteTransferButton, hasTransit ? 2 : 1);

        if (_transfer.TransitStorageLocation is { } transitLocation)
        {
            TransitLocationLabel.Text = $"{transitLocation.Address} · {transitLocation.Name}";
        }

        SetAvailableActions();
    }

    private void ShowTransitBalances()
    {
        TransitBalancesLayout.Children.Clear();
        TransitContentsTitleLabel.Text =
            $"Содержимое транзитной ячейки · {_transitBalances.Count}";
        EmptyTransitContentsLabel.IsVisible = _transitBalances.Count == 0;

        foreach (var balance in _transitBalances)
        {
            var unit = string.IsNullOrWhiteSpace(balance.UnitOfMeasure)
                ? " —"
                : $" {balance.UnitOfMeasure}";
            var layout = new Grid { ColumnSpacing = 8 };
            layout.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            layout.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

            var nameLabel = new Label
            {
                Text = balance.SkuName,
                FontAttributes = FontAttributes.Bold,
                FontSize = 17,
                LineBreakMode = LineBreakMode.TailTruncation
            };
            var quantityLabel = new Label
            {
                Text = $"{balance.Quantity:0.###}{unit}",
                FontAttributes = FontAttributes.Bold,
                FontSize = 17
            };
            Grid.SetColumn(quantityLabel, 1);
            layout.Children.Add(nameLabel);
            layout.Children.Add(quantityLabel);

            var card = new Border { Padding = 12, Content = layout };
            var tap = new TapGestureRecognizer();
            tap.Tapped += async (_, _) => await OpenTransitMovementAsync(
                TransitInventoryTransferMovementMode.Put,
                balance);
            card.GestureRecognizers.Add(tap);
            TransitBalancesLayout.Children.Add(card);
        }
    }

    private MovementListItem MapMovement(MobileInventoryTransferMovementResponse movement)
    {
        var unit = string.IsNullOrWhiteSpace(movement.UnitOfMeasure)
                ? " —"
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
        if (sender is VisualElement element)
        {
            AndroidFocus.Suppress(element);
        }
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

using System.Globalization;
using Wms.Contracts.Mobile.V1;
using Wms.Mobile.Scanning;
using Wms.Mobile.Services;

namespace Wms.Mobile;

public partial class DirectInventoryTransferPage : ContentPage
{
    private readonly MobileApiClient _apiClient;
    private readonly ILifecycleBarcodeScanner _intentScanner;
    private readonly MobileInventoryTransferSummaryResponse _transfer;
    private readonly Action<MobileMoveDirectInventoryTransferResponse> _movementCompleted;
    private MobileStorageLocationResponse? _sourceLocation;
    private MobileDirectTransferSkuResponse? _sku;
    private double? _quantity;
    private MobileStorageLocationResponse? _destinationLocation;
    private MobileMoveDirectInventoryTransferResponse? _confirmedMovement;
    private Guid? _pendingMoveRequestId;
    private bool _scannerSubscribed;
    private bool _resolving;

    public DirectInventoryTransferPage(
        MobileApiClient apiClient,
        ILifecycleBarcodeScanner intentScanner,
        MobileInventoryTransferSummaryResponse transfer,
        Action<MobileMoveDirectInventoryTransferResponse> movementCompleted)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _intentScanner = intentScanner;
        _transfer = transfer;
        _movementCompleted = movementCompleted;

        TransferNumberLabel.Text = $"Перемещение {transfer.Number}";
        TransferContextLabel.Text = $"Склад: {transfer.WarehouseName}\nСтатус: {GetStatusText(transfer.Status)}";
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (_scannerSubscribed)
        {
            return;
        }

        _intentScanner.ScanReceived += OnScanReceived;
        _scannerSubscribed = true;
    }

    protected override void OnDisappearing()
    {
        if (_scannerSubscribed)
        {
            _intentScanner.ScanReceived -= OnScanReceived;
            _scannerSubscribed = false;
        }

        base.OnDisappearing();
    }

    private void OnScanReceived(object? sender, BarcodeScanEvent scanEvent)
    {
        MainThread.BeginInvokeOnMainThread(async () => await ResolveScanAsync(scanEvent.Value));
    }

    private async Task ResolveScanAsync(string barcode)
    {
        if (_resolving || _destinationLocation is not null)
        {
            return;
        }

        if (_sku is not null && _quantity is null)
        {
            ErrorLabel.Text = "Сначала укажите количество.";
            return;
        }

        _resolving = true;
        SetBusy(true);
        ErrorLabel.Text = string.Empty;

        try
        {
            if (_sourceLocation is null)
            {
                _sourceLocation = await _apiClient.ResolveStorageLocationAsync(
                    barcode,
                    _transfer.WarehouseId,
                    MobileStorageLocationContext.Storage);

                SourceLocationLabel.Text =
                    $"{_sourceLocation.Address} · {_sourceLocation.Name}";
                SourceCard.IsVisible = true;
                StepLabel.Text = "2. Товар";
                InstructionLabel.Text = "Отсканируйте штрихкод товара.";
            }
            else if (_sku is null)
            {
                _sku = await _apiClient.ResolveDirectTransferSkuAsync(
                    _transfer.Id,
                    _sourceLocation.Id,
                    barcode);
                var unit = string.IsNullOrWhiteSpace(_sku.UnitOfMeasure)
                    ? string.Empty
                    : $" {_sku.UnitOfMeasure}";
                SkuLabel.Text = $"{_sku.Name}\nКод: {_sku.Code}";
                AvailableQuantityLabel.Text =
                    $"Доступно: {_sku.AvailableQuantity:0.###}{unit}";
                SkuCard.IsVisible = true;
                QuantityPanel.IsVisible = true;
                StepLabel.Text = "3. Количество";
                InstructionLabel.Text = "Введите количество перемещения.";
                QuantityEntry.Focus();
            }
            else
            {
                var destinationLocation = await _apiClient.ResolveStorageLocationAsync(
                    barcode,
                    _transfer.WarehouseId,
                    MobileStorageLocationContext.Storage);
                if (destinationLocation.Id == _sourceLocation.Id)
                {
                    ErrorLabel.Text =
                        "Ячейка назначения должна отличаться от исходной ячейки.";
                    return;
                }

                _destinationLocation = destinationLocation;
                DestinationLocationLabel.Text =
                    $"{destinationLocation.Address} · {destinationLocation.Name}";
                DestinationCard.IsVisible = true;
                StepLabel.Text = "Проверьте перемещение";
                InstructionLabel.Text =
                    "До подтверждения складские остатки не изменены.";
                ConfirmButton.IsVisible = true;
                ConfirmButton.Unfocus();
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
            _resolving = false;
        }
    }

    private void SetBusy(bool isBusy)
    {
        ProgressIndicator.IsVisible = isBusy;
        ProgressIndicator.IsRunning = isBusy;
        AcceptQuantityButton.IsEnabled = !isBusy && _quantity is null;
        ConfirmButton.IsEnabled = !isBusy
            && _destinationLocation is not null
            && _confirmedMovement is null;
    }

    private void OnAcceptQuantityClicked(object? sender, EventArgs e)
    {
        if (_sku is null)
        {
            return;
        }

        var value = QuantityEntry.Text?.Trim().Replace(',', '.');
        QuantityErrorLabel.Text = string.Empty;
        if (!double.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var quantity)
            || !double.IsFinite(quantity)
            || quantity <= 0)
        {
            QuantityErrorLabel.Text = "Введите количество больше нуля.";
            return;
        }

        if (quantity > _sku.AvailableQuantity)
        {
            QuantityErrorLabel.Text =
                $"Недостаточно товара. Доступно: {_sku.AvailableQuantity:0.###}.";
            return;
        }

        _quantity = quantity;
        ErrorLabel.Text = string.Empty;
        QuantityErrorLabel.Text = string.Empty;
        QuantityEntry.Unfocus();
        QuantityPanel.IsVisible = false;
        SelectedQuantityLabel.Text = $"Количество: {quantity:0.###}";
        SelectedQuantityLabel.IsVisible = true;
        StepLabel.Text = "4. Ячейка назначения";
        InstructionLabel.Text = "Отсканируйте QR ячейки назначения.";
    }

    private async void OnConfirmClicked(object? sender, EventArgs e)
    {
        if (_sourceLocation is null
            || _sku is null
            || _quantity is not double quantity
            || _destinationLocation is null)
        {
            return;
        }

        _pendingMoveRequestId ??= Guid.NewGuid();
        SetBusy(true);
        ErrorLabel.Text = string.Empty;

        try
        {
            _confirmedMovement = await _apiClient.MoveDirectAsync(
                _transfer.Id,
                _sourceLocation.Id,
                _destinationLocation.Id,
                _sku.Id,
                quantity,
                _pendingMoveRequestId.Value);
            _pendingMoveRequestId = null;
            _movementCompleted(_confirmedMovement);
            await Navigation.PopAsync();
        }
        catch (MobileApiException exception)
        {
            _pendingMoveRequestId = null;
            ErrorLabel.Text = exception.Message;
        }
        catch (HttpRequestException)
        {
            ErrorLabel.Text =
                "Ответ сервера не получен. Повторите подтверждение.";
            ConfirmButton.Text = "Повторить подтверждение";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void OnConfirmButtonLoaded(object? sender, EventArgs e) =>
        DisableAndroidButtonFocus(ConfirmButton);

    private static void DisableAndroidButtonFocus(Button mauiButton)
    {
#if ANDROID
        if (mauiButton.Handler?.PlatformView is Android.Widget.Button button)
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
}

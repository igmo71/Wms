using System.Globalization;
using Wms.Contracts.Mobile.V1;
using Wms.Mobile.Scanning;
using Wms.Mobile.Services;

namespace Wms.Mobile;

public partial class DirectInventoryTransferPage : ContentPage
{
    private readonly MobileApiClient _apiClient;
    private readonly IOperationalBarcodeScanner _scanner;
    private readonly MobileInventoryTransferSummaryResponse _transfer;
    private readonly Action<MobileMoveDirectInventoryTransferResponse> _movementCompleted;
    private MobileStorageLocationResponse? _sourceLocation;
    private MobileDirectTransferSkuResponse? _sku;
    private double? _quantity;
    private MobileStorageLocationResponse? _destinationLocation;
    private MobileMoveDirectInventoryTransferResponse? _confirmedMovement;
    private Guid? _pendingMoveRequestId;
    private CancellationTokenSource? _skuSearchCancellation;
    private bool _scannerSubscribed;
    private bool _resolving;

    public DirectInventoryTransferPage(
        MobileApiClient apiClient,
        IOperationalBarcodeScanner scanner,
        MobileInventoryTransferSummaryResponse transfer,
        Action<MobileMoveDirectInventoryTransferResponse> movementCompleted)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _scanner = scanner;
        _transfer = transfer;
        _movementCompleted = movementCompleted;
        CameraScannerView.Configure(scanner);

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

        _scanner.ScanReceived += OnScanReceived;
        _scannerSubscribed = true;
        _ = UpdateCameraAsync();
    }

    protected override void OnDisappearing()
    {
        _skuSearchCancellation?.Cancel();
        SkuSearchEntry.Unfocus();
        CameraScannerView.Stop();

        if (_scannerSubscribed)
        {
            _scanner.ScanReceived -= OnScanReceived;
            _scannerSubscribed = false;
        }

        base.OnDisappearing();
    }

    private void OnScanReceived(object? sender, BarcodeScanEvent scanEvent)
    {
        MainThread.BeginInvokeOnMainThread(async () => await ResolveScanAsync(scanEvent.Value));
    }

    private bool IsScanExpected() => !_resolving
        && _destinationLocation is null
        && !SkuSearchPanel.IsVisible
        && (_sourceLocation is null
            || _sku is null
            || (_quantity is not null && _destinationLocation is null));

    private async Task UpdateCameraAsync()
    {
        if (_scanner.ActiveSource is null)
        {
            CameraScannerView.Stop();
            ErrorLabel.Text = "На устройстве не найден доступный сканер.";
            return;
        }

        if (_scanner.ActiveSource == BarcodeScanSource.Camera && IsScanExpected())
        {
            await CameraScannerView.StartAsync();
        }
        else
        {
            CameraScannerView.Stop();
        }
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
                StepLabel.Text = "Товар";
                InstructionLabel.Text = "Отсканируйте штрихкод товара.";
                SkuSearchPrompt.IsVisible = true;
            }
            else if (_sku is null)
            {
                var sku = await _apiClient.ResolveDirectTransferSkuAsync(
                    _transfer.Id,
                    _sourceLocation.Id,
                    barcode);
                ApplySku(sku, focusQuantity: true);
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
            await UpdateCameraAsync();
        }
    }

    private void OnOpenSkuSearchTapped(object? sender, TappedEventArgs e)
    {
        if (_sourceLocation is null || _sku is not null || _resolving)
        {
            return;
        }

        SkuSearchPrompt.IsVisible = false;
        SkuSearchPanel.IsVisible = true;
        CameraScannerView.Stop();
        InstructionLabel.Text = "Введите наименование, код или штрихкод.";
        SkuSearchStatusLabel.Text = "Введите не менее двух символов.";
        Dispatcher.Dispatch(() => SkuSearchEntry.Focus());
    }

    private async void OnCancelSkuSearchTapped(object? sender, TappedEventArgs e)
    {
        CloseSkuSearch(showPrompt: true);
        await UpdateCameraAsync();
    }

    private async void OnSkuSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        _skuSearchCancellation?.Cancel();
        _skuSearchCancellation = null;
        SkuSearchResults.Children.Clear();

        var query = e.NewTextValue?.Trim() ?? string.Empty;
        if (_sourceLocation is null || _sku is not null || query.Length < 2)
        {
            SetSkuSearchBusy(false);
            InstructionLabel.Text = "Введите наименование, код или штрихкод.";
            SkuSearchStatusLabel.Text = "Введите не менее двух символов.";
            return;
        }

        var cancellation = new CancellationTokenSource();
        _skuSearchCancellation = cancellation;
        InstructionLabel.Text = "Ищем товар...";
        SkuSearchStatusLabel.Text = string.Empty;

        try
        {
            await Task.Delay(300, cancellation.Token);
            SetSkuSearchBusy(true);

            var results = await _apiClient.SearchDirectTransferSkusAsync(
                _transfer.Id,
                _sourceLocation.Id,
                query,
                cancellation.Token);
            if (cancellation.IsCancellationRequested
                || !ReferenceEquals(_skuSearchCancellation, cancellation)
                || _sku is not null)
            {
                return;
            }

            ShowSkuSearchResults(results);
        }
        catch (OperationCanceledException)
        {
            // Новый текст или сканирование отменяет устаревший поиск.
        }
        catch (MobileApiException exception)
        {
            SkuSearchStatusLabel.Text = exception.Message;
        }
        catch (HttpRequestException)
        {
            SkuSearchStatusLabel.Text = "Сервер WMS недоступен.";
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(exception);
            SkuSearchStatusLabel.Text = "Не удалось выполнить поиск товара.";
        }
        finally
        {
            if (ReferenceEquals(_skuSearchCancellation, cancellation))
            {
                _skuSearchCancellation = null;
                SetSkuSearchBusy(false);
            }

            cancellation.Dispose();
        }
    }

    private void ShowSkuSearchResults(
        IReadOnlyList<MobileDirectTransferSkuSearchResponse> results)
    {
        SkuSearchResults.Children.Clear();
        InstructionLabel.Text = results.Count == 10
            ? "Найдено: 10. Уточните запрос, если вариантов слишком много."
            : $"Найдено: {results.Count}.";
        if (results.Count == 0)
        {
            SkuSearchStatusLabel.Text = "В исходной ячейке товар не найден.";
            return;
        }

        SkuSearchStatusLabel.Text = string.Empty;
        foreach (var result in results)
        {
            var unit = string.IsNullOrWhiteSpace(result.UnitOfMeasure)
                ? string.Empty
                : $" {result.UnitOfMeasure}";
            var resultLayout = new VerticalStackLayout { Spacing = 3 };
            resultLayout.Children.Add(new Label
            {
                Text = result.Name,
                FontSize = 17,
                FontAttributes = result.IsExactMatch
                    ? FontAttributes.Bold
                    : FontAttributes.None
            });
            resultLayout.Children.Add(new Label
            {
                Text = $"Код: {result.Code} · Доступно: {result.AvailableQuantity:0.###}{unit}",
                FontSize = 15
            });

            var resultBorder = new Border
            {
                Padding = 12,
                Content = resultLayout
            };
            var tapGesture = new TapGestureRecognizer();
            tapGesture.Tapped += async (_, _) => await SelectSkuSearchResultAsync(result);
            resultBorder.GestureRecognizers.Add(tapGesture);
            SkuSearchResults.Children.Add(resultBorder);
        }
    }

    private async Task SelectSkuSearchResultAsync(
        MobileDirectTransferSkuSearchResponse result)
    {
        if (_resolving || _sku is not null)
        {
            return;
        }

        SkuSearchEntry.Unfocus();
        await SkuSearchEntry.HideSoftInputAsync(CancellationToken.None);
        ApplySku(new MobileDirectTransferSkuResponse(
            result.Id,
            result.Code,
            result.Name,
            result.UnitOfMeasure,
            result.AvailableQuantity),
            focusQuantity: false);
    }

    private void ApplySku(MobileDirectTransferSkuResponse sku, bool focusQuantity)
    {
        _sku = sku;
        CloseSkuSearch(showPrompt: false);

        var unit = string.IsNullOrWhiteSpace(sku.UnitOfMeasure)
            ? string.Empty
            : $" {sku.UnitOfMeasure}";
        SkuLabel.Text = $"{sku.Name}\nКод: {sku.Code}";
        AvailableQuantityLabel.Text =
            $"Доступно: {sku.AvailableQuantity:0.###}{unit}";
        SkuCard.IsVisible = true;
        QuantityPanel.IsVisible = true;
        StepLabel.Text = "Количество";
        InstructionLabel.Text = "Введите количество перемещения.";
        if (focusQuantity)
        {
            Dispatcher.Dispatch(() => QuantityEntry.Focus());
        }
    }

    private void CloseSkuSearch(bool showPrompt)
    {
        _skuSearchCancellation?.Cancel();
        _skuSearchCancellation = null;
        SetSkuSearchBusy(false);
        SkuSearchEntry.Unfocus();
        SkuSearchEntry.Text = string.Empty;
        SkuSearchResults.Children.Clear();
        SkuSearchPanel.IsVisible = false;
        SkuSearchPrompt.IsVisible = showPrompt
            && _sourceLocation is not null
            && _sku is null;
        if (showPrompt)
        {
            InstructionLabel.Text = "Отсканируйте штрихкод товара.";
        }
    }

    private void SetSkuSearchBusy(bool isBusy)
    {
        SkuSearchIndicator.IsVisible = isBusy;
        SkuSearchIndicator.IsRunning = isBusy;
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

    private async void OnAcceptQuantityClicked(object? sender, EventArgs e)
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
        StepLabel.Text = "Ячейка назначения";
        InstructionLabel.Text = "Отсканируйте QR ячейки назначения.";
        await UpdateCameraAsync();
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

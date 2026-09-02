using System.Globalization;
using Wms.Contracts.Mobile.V1;
using Wms.Mobile.Scanning;
using Wms.Mobile.Services;

namespace Wms.Mobile;

public enum TransitInventoryTransferMovementMode
{
    Pick,
    Put
}

public partial class TransitInventoryTransferMovementPage : ContentPage
{
    private readonly MobileApiClient _apiClient;
    private readonly IOperationalBarcodeScanner _scanner;
    private readonly MobileInventoryTransferSummaryResponse _transfer;
    private readonly TransitInventoryTransferMovementMode _mode;
    private readonly Action<MobileTransitInventoryTransferMovementResponse> _completed;
    private MobileStorageLocationResponse? _storageLocation;
    private MobileDirectTransferSkuResponse? _sku;
    private decimal? _quantity;
    private Guid? _pendingRequestId;
    private CancellationTokenSource? _searchCancellation;
    private bool _scannerSubscribed;
    private bool _busy;

    public TransitInventoryTransferMovementPage(
        MobileApiClient apiClient,
        IOperationalBarcodeScanner scanner,
        MobileInventoryTransferSummaryResponse transfer,
        TransitInventoryTransferMovementMode mode,
        IReadOnlyList<MobileInventoryTransferSkuBalanceResponse> transitBalances,
        MobileInventoryTransferSkuBalanceResponse? selectedSku,
        Action<MobileTransitInventoryTransferMovementResponse> completed)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _scanner = scanner;
        _transfer = transfer;
        _mode = mode;
        _completed = completed;
        CameraScannerView.Configure(scanner);

        var transitLocation = transfer.TransitStorageLocation
            ?? throw new ArgumentException("У перемещения нет транзитной ячейки.", nameof(transfer));
        TransferNumberLabel.Text = $"Перемещение {transfer.Number}";
        TransitLocationLabel.Text = $"{transitLocation.Address} · {transitLocation.Name}";

        if (mode == TransitInventoryTransferMovementMode.Pick)
        {
            Title = "В транзит";
            StepLabel.Text = "Исходная ячейка";
            InstructionLabel.Text = "Отсканируйте QR исходной ячейки.";
        }
        else
        {
            Title = "Из транзита";
            ShowSkuStep();
            ShowTransitSkuChoices(transitBalances);
            if (selectedSku is not null)
            {
                ApplySku(ToSku(selectedSku), focusQuantity: false);
            }
        }
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
        _searchCancellation?.Cancel();
        SkuSearchEntry.Unfocus();
        CameraScannerView.Stop();
        if (_scannerSubscribed)
        {
            _scanner.ScanReceived -= OnScanReceived;
            _scannerSubscribed = false;
        }

        base.OnDisappearing();
    }

    private void OnScanReceived(object? sender, BarcodeScanEvent scanEvent) =>
        MainThread.BeginInvokeOnMainThread(async () => await ResolveScanAsync(scanEvent.Value));

    private bool IsScanExpected()
    {
        if (_busy
            || ConfirmButton.IsVisible
            || SkuSearchPanel.IsVisible
            || (_sku is not null && _quantity is null))
        {
            return false;
        }

        return _mode == TransitInventoryTransferMovementMode.Pick
            ? _storageLocation is null || _sku is null
            : _sku is null || (_quantity is not null && _storageLocation is null);
    }

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
        if (_busy || ConfirmButton.IsVisible)
        {
            return;
        }

        if (_sku is not null && _quantity is null)
        {
            ErrorLabel.Text = "Сначала укажите количество.";
            return;
        }

        SetBusy(true);
        ErrorLabel.Text = string.Empty;

        try
        {
            if (_mode == TransitInventoryTransferMovementMode.Pick
                && _storageLocation is null)
            {
                _storageLocation = await ResolveStorageLocationAsync(barcode);
                ShowStorageLocation("Исходная ячейка", _storageLocation);
                ShowSkuStep();
            }
            else if (_sku is null)
            {
                var sku = _mode == TransitInventoryTransferMovementMode.Pick
                    ? await _apiClient.ResolveDirectTransferSkuAsync(
                        _transfer.Id,
                        _storageLocation!.Id,
                        barcode)
                    : await _apiClient.ResolveTransitTransferSkuAsync(_transfer.Id, barcode);
                if (sku.AvailableQuantity <= 0)
                {
                    ErrorLabel.Text = "В исходной ячейке этого товара нет.";
                    return;
                }

                ApplySku(sku, focusQuantity: true);
            }
            else if (_mode == TransitInventoryTransferMovementMode.Put
                && _storageLocation is null)
            {
                _storageLocation = await ResolveStorageLocationAsync(barcode);
                ShowStorageLocation("Ячейка назначения", _storageLocation);
                ShowReview();
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
            await UpdateCameraAsync();
        }
    }

    private Task<MobileStorageLocationResponse> ResolveStorageLocationAsync(string barcode) =>
        _apiClient.ResolveStorageLocationAsync(
            barcode,
            _transfer.WarehouseId,
            MobileStorageLocationContext.Storage);

    private void ShowSkuStep()
    {
        StepLabel.Text = "Товар";
        InstructionLabel.Text = _mode == TransitInventoryTransferMovementMode.Put
            ? "Отсканируйте товар или выберите его из содержимого."
            : "Отсканируйте штрихкод товара.";
        SkuSearchPrompt.IsVisible = true;
        TransitSkuChoicesPanel.IsVisible = _mode == TransitInventoryTransferMovementMode.Put
            && TransitSkuChoices.Children.Count > 0;
    }

    private void ShowTransitSkuChoices(
        IReadOnlyList<MobileInventoryTransferSkuBalanceResponse> balances)
    {
        TransitSkuChoices.Children.Clear();
        foreach (var balance in balances)
        {
            var unit = string.IsNullOrWhiteSpace(balance.UnitOfMeasure)
                ? string.Empty
                : $" {balance.UnitOfMeasure}";
            var layout = new Grid { ColumnSpacing = 8 };
            layout.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            layout.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            var name = new Label { Text = balance.SkuName, FontSize = 17 };
            var quantity = new Label
            {
                Text = $"{balance.Quantity:0.###}{unit}",
                FontAttributes = FontAttributes.Bold,
                FontSize = 17
            };
            Grid.SetColumn(quantity, 1);
            layout.Children.Add(name);
            layout.Children.Add(quantity);

            var card = new Border { Padding = 12, Content = layout };
            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) => ApplySku(ToSku(balance), focusQuantity: false);
            card.GestureRecognizers.Add(tap);
            TransitSkuChoices.Children.Add(card);
        }

        TransitSkuChoicesPanel.IsVisible = balances.Count > 0;
    }

    private static MobileDirectTransferSkuResponse ToSku(
        MobileInventoryTransferSkuBalanceResponse balance) => new(
        balance.StockKeepingUnitId,
        balance.SkuCode,
        balance.SkuName,
        balance.UnitOfMeasure,
        balance.Quantity);

    private void ApplySku(MobileDirectTransferSkuResponse sku, bool focusQuantity)
    {
        if (_sku is not null)
        {
            return;
        }

        _sku = sku;
        CameraScannerView.Stop();
        CloseSkuSearch(showPrompt: false);
        TransitSkuChoicesPanel.IsVisible = false;

        var unit = string.IsNullOrWhiteSpace(sku.UnitOfMeasure)
            ? string.Empty
            : $" {sku.UnitOfMeasure}";
        SkuLabel.Text = $"{sku.Name}\nКод: {sku.Code}";
        AvailableQuantityLabel.Text = $"Доступно: {sku.AvailableQuantity:0.###}{unit}";
        SkuCard.IsVisible = true;
        QuantityPanel.IsVisible = true;
        StepLabel.Text = "Количество";
        InstructionLabel.Text = "Введите количество перемещения.";
        if (focusQuantity)
        {
            Dispatcher.Dispatch(() => QuantityEntry.Focus());
        }
    }

    private async void OnAcceptQuantityClicked(object? sender, EventArgs e)
    {
        if (_sku is null)
        {
            return;
        }

        var value = QuantityEntry.Text?.Trim().Replace(',', '.');
        QuantityErrorLabel.Text = string.Empty;
        if (!decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var quantity)
            || !WarehouseQuantityInput.IsSupported(quantity)
            || quantity <= 0)
        {
            QuantityErrorLabel.Text = "Введите количество больше нуля.";
            return;
        }

        if (quantity > _sku.AvailableQuantity)
        {
            QuantityErrorLabel.Text = $"Недостаточно товара. Доступно: {_sku.AvailableQuantity:0.###}.";
            return;
        }

        _quantity = quantity;
        QuantityEntry.Unfocus();
        QuantityPanel.IsVisible = false;
        SelectedQuantityLabel.Text = $"Количество: {quantity:0.###}";
        SelectedQuantityLabel.IsVisible = true;

        if (_mode == TransitInventoryTransferMovementMode.Pick)
        {
            ShowReview();
        }
        else
        {
            StepLabel.Text = "Ячейка назначения";
            InstructionLabel.Text = "Отсканируйте QR ячейки назначения.";
            await UpdateCameraAsync();
        }
    }

    private void ShowReview()
    {
        StepLabel.Text = "Проверьте перемещение";
        InstructionLabel.Text = "До подтверждения складские остатки не изменены.";
        ConfirmButton.Text = _mode == TransitInventoryTransferMovementMode.Pick
            ? "В транзит"
            : "Из транзита";
        ConfirmButton.IsVisible = true;
        ConfirmButton.IsEnabled = !_busy;
        ConfirmButton.Unfocus();
    }

    private async void OnConfirmClicked(object? sender, EventArgs e)
    {
        if (_storageLocation is null || _sku is null || _quantity is not decimal quantity)
        {
            return;
        }

        _pendingRequestId ??= Guid.NewGuid();
        SetBusy(true);
        ErrorLabel.Text = string.Empty;

        try
        {
            var result = _mode == TransitInventoryTransferMovementMode.Pick
                ? await _apiClient.PickToTransitAsync(
                    _transfer.Id,
                    _storageLocation.Id,
                    _sku.Id,
                    quantity,
                    _pendingRequestId.Value)
                : await _apiClient.PutFromTransitAsync(
                    _transfer.Id,
                    _storageLocation.Id,
                    _sku.Id,
                    quantity,
                    _pendingRequestId.Value);
            _pendingRequestId = null;
            _completed(result);
            await Navigation.PopAsync();
        }
        catch (MobileApiException exception)
        {
            _pendingRequestId = null;
            ErrorLabel.Text = exception.Message;
        }
        catch (HttpRequestException)
        {
            ErrorLabel.Text = "Ответ сервера не получен. Повторите подтверждение.";
            ConfirmButton.Text = "Повторить";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void ShowStorageLocation(string title, MobileStorageLocationResponse location)
    {
        StorageLocationTitleLabel.Text = title;
        StorageLocationLabel.Text = $"{location.Address} · {location.Name}";
        StorageLocationCard.IsVisible = true;
    }

    private void OnOpenSkuSearchTapped(object? sender, TappedEventArgs e)
    {
        if (_sku is not null || _busy)
        {
            return;
        }

        SkuSearchPrompt.IsVisible = false;
        SkuSearchPanel.IsVisible = true;
        TransitSkuChoicesPanel.IsVisible = false;
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
        _searchCancellation?.Cancel();
        _searchCancellation = null;
        SkuSearchResults.Children.Clear();

        var query = e.NewTextValue?.Trim() ?? string.Empty;
        if (_sku is not null || query.Length < 2)
        {
            SetSearchBusy(false);
            InstructionLabel.Text = "Введите наименование, код или штрихкод.";
            SkuSearchStatusLabel.Text = "Введите не менее двух символов.";
            return;
        }

        if (_mode == TransitInventoryTransferMovementMode.Pick && _storageLocation is null)
        {
            return;
        }

        var cancellation = new CancellationTokenSource();
        _searchCancellation = cancellation;
        InstructionLabel.Text = "Ищем товар...";
        SkuSearchStatusLabel.Text = string.Empty;

        try
        {
            await Task.Delay(300, cancellation.Token);
            SetSearchBusy(true);
            var results = _mode == TransitInventoryTransferMovementMode.Pick
                ? await _apiClient.SearchDirectTransferSkusAsync(
                    _transfer.Id,
                    _storageLocation!.Id,
                    query,
                    cancellation.Token)
                : await _apiClient.SearchTransitTransferSkusAsync(
                    _transfer.Id,
                    query,
                    cancellation.Token);
            if (!cancellation.IsCancellationRequested
                && ReferenceEquals(_searchCancellation, cancellation)
                && _sku is null)
            {
                ShowSearchResults(results);
            }
        }
        catch (OperationCanceledException)
        {
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
            if (ReferenceEquals(_searchCancellation, cancellation))
            {
                _searchCancellation = null;
                SetSearchBusy(false);
            }

            cancellation.Dispose();
        }
    }

    private void ShowSearchResults(IReadOnlyList<MobileDirectTransferSkuSearchResponse> results)
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
            var layout = new VerticalStackLayout { Spacing = 3 };
            layout.Children.Add(new Label
            {
                Text = result.Name,
                FontSize = 17,
                FontAttributes = result.IsExactMatch ? FontAttributes.Bold : FontAttributes.None
            });
            layout.Children.Add(new Label
            {
                Text = $"Код: {result.Code} · Доступно: {result.AvailableQuantity:0.###}{unit}",
                FontSize = 15
            });
            var card = new Border { Padding = 12, Content = layout };
            var tap = new TapGestureRecognizer();
            tap.Tapped += async (_, _) => await SelectSearchResultAsync(result);
            card.GestureRecognizers.Add(tap);
            SkuSearchResults.Children.Add(card);
        }
    }

    private async Task SelectSearchResultAsync(MobileDirectTransferSkuSearchResponse result)
    {
        if (_sku is not null || _busy)
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

    private void CloseSkuSearch(bool showPrompt)
    {
        _searchCancellation?.Cancel();
        _searchCancellation = null;
        SetSearchBusy(false);
        SkuSearchEntry.Unfocus();
        SkuSearchEntry.Text = string.Empty;
        SkuSearchResults.Children.Clear();
        SkuSearchPanel.IsVisible = false;
        SkuSearchPrompt.IsVisible = showPrompt && _sku is null;
        TransitSkuChoicesPanel.IsVisible = showPrompt
            && _mode == TransitInventoryTransferMovementMode.Put
            && TransitSkuChoices.Children.Count > 0;
        if (showPrompt)
        {
            ShowSkuStep();
        }
    }

    private void SetSearchBusy(bool isBusy)
    {
        SkuSearchIndicator.IsVisible = isBusy;
        SkuSearchIndicator.IsRunning = isBusy;
    }

    private void SetBusy(bool isBusy)
    {
        _busy = isBusy;
        ProgressIndicator.IsVisible = isBusy;
        ProgressIndicator.IsRunning = isBusy;
        AcceptQuantityButton.IsEnabled = !isBusy && _quantity is null;
        ConfirmButton.IsEnabled = !isBusy && ConfirmButton.IsVisible;
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
}

using System.Collections.ObjectModel;
using System.Globalization;
using Wms.Contracts.Mobile.V1;
using Wms.Mobile.Scanning;
using Wms.Mobile.Services;

namespace Wms.Mobile;

public partial class InventoryCountDetailsPage : ContentPage
{
    private const int VisibleSearchResultCount = 10;

    private readonly MobileApiClient _apiClient;
    private readonly IOperationalBarcodeScanner _scanner;
    private readonly Dictionary<Guid, Entry> _quantityEntries = [];
    private MobileInventoryCountDetailsResponse _details;
    private int _searchVersion;
    private InventoryCountPageMode _mode = InventoryCountPageMode.Scanning;
    private InventoryCountItemViewState? _editingItem;
    private Entry? _activeQuantityEntry;
    private bool _scannerSubscribed;
    private bool _busy;
    private Guid? _pendingScanRequestId;
    private string? _pendingBarcode;
    private Guid? _pendingQuantityRequestId;
    private Guid? _pendingPostRequestId;
    private Guid? _pendingDeleteRequestId;
    private Guid? _pendingRemoveRequestId;
    private Guid? _pendingRemoveItemId;

    public InventoryCountDetailsPage(
        MobileApiClient apiClient,
        IOperationalBarcodeScanner scanner,
        MobileInventoryCountDetailsResponse details)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _scanner = scanner;
        _details = details;
        CameraScannerView.Configure(scanner);
        ApplyDetails(details);
    }

    public ObservableCollection<InventoryCountItemViewState> ItemStates { get; } = [];

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (!_scannerSubscribed)
        {
            _scanner.ScanReceived += OnScanReceived;
            _scannerSubscribed = true;
        }
        _ = UpdateCameraAsync();
    }

    protected override void OnDisappearing()
    {
        _searchVersion++;
        SkuSearchEntry.Unfocus();
        _activeQuantityEntry?.Unfocus();
        CameraScannerView.Stop();
        if (_scannerSubscribed)
        {
            _scanner.ScanReceived -= OnScanReceived;
            _scannerSubscribed = false;
        }
        base.OnDisappearing();
    }

    private bool IsDraft => _details.Count.Status == MobileInventoryCountStatus.Draft;
    private bool IsScanExpected => IsDraft
        && !_busy
        && _mode == InventoryCountPageMode.Scanning;

    private void OnScanReceived(object? sender, BarcodeScanEvent scanEvent) =>
        MainThread.BeginInvokeOnMainThread(async () => await IncrementScanAsync(scanEvent.Value));

    private async Task IncrementScanAsync(string barcode)
    {
        if (!IsScanExpected)
            return;
        if (_pendingBarcode is not null && _pendingBarcode != barcode)
        {
            ErrorLabel.Text = "Повторите предыдущий штрихкод: ответ сервера не был получен.";
            return;
        }

        _pendingBarcode ??= barcode;
        _pendingScanRequestId ??= Guid.NewGuid();
        SetBusy(true);
        ErrorLabel.Text = string.Empty;
        try
        {
            var response = await _apiClient.IncrementInventoryCountSkuAsync(
                _details.Count.Id,
                barcode,
                _pendingScanRequestId.Value);
            _pendingScanRequestId = null;
            _pendingBarcode = null;
            ApplyDetails(response.Details);
            AccentItem(response.Item.StockKeepingUnitId, "+1");
            ShowItem(response.Item.StockKeepingUnitId);
            InstructionLabel.Text = "Принято +1. Сканируйте следующий товар.";
        }
        catch (MobileApiException exception)
        {
            _pendingScanRequestId = null;
            _pendingBarcode = null;
            ErrorLabel.Text = exception.Message;
        }
        catch (HttpRequestException)
        {
            ErrorLabel.Text = "Ответ сервера не получен. Повторно отсканируйте этот же товар.";
        }
        finally
        {
            SetBusy(false);
            await UpdateCameraAsync();
        }
    }

    private void OnOpenSkuSearchTapped(object? sender, TappedEventArgs e)
    {
        if (!IsScanExpected)
            return;

        SetMode(InventoryCountPageMode.Searching);
        CameraScannerView.Stop();
        StepLabel.Text = "Ручной выбор товара";
        InstructionLabel.Text = "Введите наименование, код или штрихкод.";
        Dispatcher.Dispatch(() => SkuSearchEntry.Focus());
    }

    private async void OnCancelSkuSearchTapped(object? sender, TappedEventArgs e)
    {
        CloseSearch();
        await ReturnToScanningAsync();
    }

    private async void OnSkuSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        var searchVersion = ++_searchVersion;
        SetSearchBusy(false);
        SkuSearchResults.Children.Clear();
        if (_mode != InventoryCountPageMode.Searching)
            return;

        var query = e.NewTextValue?.Trim() ?? string.Empty;
        if (query.Length < 2)
        {
            SkuSearchStatusLabel.Text = "Введите не менее двух символов.";
            return;
        }

        try
        {
            await Task.Delay(300);
            if (!IsCurrentSearch(searchVersion))
                return;

            SetSearchBusy(true);
            var results = await _apiClient.SearchInventoryCountSkusAsync(
                _details.Count.Id,
                query,
                CancellationToken.None);
            if (IsCurrentSearch(searchVersion))
                ShowSearchResults(results);
        }
        catch (MobileApiException exception)
        {
            if (IsCurrentSearch(searchVersion))
                SkuSearchStatusLabel.Text = exception.Message;
        }
        catch (HttpRequestException)
        {
            if (IsCurrentSearch(searchVersion))
                SkuSearchStatusLabel.Text = "Сервер WMS недоступен.";
        }
        finally
        {
            if (IsCurrentSearch(searchVersion))
                SetSearchBusy(false);
        }
    }

    private bool IsCurrentSearch(int searchVersion) =>
        searchVersion == _searchVersion
        && _mode == InventoryCountPageMode.Searching;

    private void ShowSearchResults(IReadOnlyList<MobileInventoryCountSkuSearchResponse> results)
    {
        SkuSearchResults.Children.Clear();
        SkuSearchStatusLabel.Text = results.Count > VisibleSearchResultCount
            ? $"Найдено: более {VisibleSearchResultCount}. Уточните запрос."
            : $"Найдено: {results.Count}.";
        foreach (var sku in results.Take(VisibleSearchResultCount))
        {
            var layout = new VerticalStackLayout { Spacing = 3 };
            layout.Children.Add(new Label
            {
                Text = sku.Name,
                FontSize = 17,
                FontAttributes = sku.IsExactMatch ? FontAttributes.Bold : FontAttributes.None
            });
            layout.Children.Add(new Label { Text = $"Код: {sku.Code} · {sku.UnitOfMeasure}", FontSize = 15 });
            var border = new Border { Padding = 12, Content = layout };
            var tap = new TapGestureRecognizer();
            tap.Tapped += async (_, _) => await SelectSkuAsync(sku);
            border.GestureRecognizers.Add(tap);
            border.Loaded += OnNonScanControlLoaded;
            SkuSearchResults.Children.Add(border);
        }
    }

    private async Task SelectSkuAsync(MobileInventoryCountSkuSearchResponse sku)
    {
        SkuSearchEntry.Unfocus();
        await SkuSearchEntry.HideSoftInputAsync(CancellationToken.None);
        CloseSearch();

        var item = ItemStates.SingleOrDefault(x => x.StockKeepingUnitId == sku.Id);
        if (item is null)
        {
            item = InventoryCountItemViewState.FromPendingSku(sku);
            ItemStates.Add(item);
            await BeginQuantityEditAsync(item, InventoryCountPageMode.EditingNew);
            ShowItem(item);
        }
        else
        {
            await BeginQuantityEditAsync(item, InventoryCountPageMode.EditingExisting);
            ShowItem(item);
        }
    }

    private async void OnPostClicked(object? sender, EventArgs e)
    {
        if (_busy || _mode != InventoryCountPageMode.Scanning)
            return;
        if (_details.Items.Any(x => x.CountedQuantity is null))
        {
            ErrorLabel.Text = "Сначала пересчитайте все ожидаемые позиции.";
            return;
        }
        if (_pendingPostRequestId is null
            && !await DisplayAlertAsync(
                "Провести инвентаризацию",
                "Остатки ячейки будут исправлены по фактическому количеству.",
                "Провести",
                "Отмена"))
            return;

        _pendingPostRequestId ??= Guid.NewGuid();
        SetBusy(true);
        try
        {
            _details = await _apiClient.PostInventoryCountAsync(
                _details.Count.Id,
                _pendingPostRequestId.Value);
            _pendingPostRequestId = null;
            await DisplayAlertAsync("Готово", "Инвентаризация проведена, ячейка освобождена.", "ОК");
            await Navigation.PopAsync();
        }
        catch (MobileApiException exception)
        {
            _pendingPostRequestId = null;
            PostButton.Text = "Провести";
            ErrorLabel.Text = exception.Message;
        }
        catch (HttpRequestException)
        {
            ErrorLabel.Text = "Ответ сервера не получен. Нажмите «Провести» повторно.";
            PostButton.Text = "Повторить проведение";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void OnDeleteDraftClicked(object? sender, EventArgs e)
    {
        if (_busy || _mode != InventoryCountPageMode.Scanning)
            return;
        if (_pendingDeleteRequestId is null
            && !await DisplayAlertAsync(
                "Удалить черновик",
                "Введённые данные будут удалены, ячейка освобождена.",
                "Удалить",
                "Оставить"))
            return;

        _pendingDeleteRequestId ??= Guid.NewGuid();
        SetBusy(true);
        try
        {
            await _apiClient.DeleteInventoryCountDraftAsync(
                _details.Count.Id,
                _pendingDeleteRequestId.Value);
            _pendingDeleteRequestId = null;
            await Navigation.PopAsync();
        }
        catch (MobileApiException exception)
        {
            _pendingDeleteRequestId = null;
            ErrorLabel.Text = exception.Message;
        }
        catch (HttpRequestException)
        {
            ErrorLabel.Text = "Ответ сервера не получен. Нажмите «Удалить черновик» повторно.";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void ApplyDetails(MobileInventoryCountDetailsResponse details)
    {
        _details = details;
        NumberLabel.Text = $"Инвентаризация {details.Count.Number}";
        LocationLabel.Text = $"{details.Count.StorageLocation.Address} · {details.Count.StorageLocation.Name}";
        var remaining = details.Count.TotalItems - details.Count.CountedItems;
        ProgressLabel.Text = $"Пересчитано: {details.Count.CountedItems} из {details.Count.TotalItems} · Осталось: {remaining}";
        SynchronizeItemStates(details.Items);
        RefreshActionAvailability();
    }

    private void SynchronizeItemStates(IReadOnlyList<MobileInventoryCountItemResponse> items)
    {
        var currentSkuIds = items.Select(x => x.StockKeepingUnitId).ToHashSet();
        for (var index = ItemStates.Count - 1; index >= 0; index--)
        {
            var state = ItemStates[index];
            if (!state.IsPending && !currentSkuIds.Contains(state.StockKeepingUnitId))
                ItemStates.RemoveAt(index);
        }

        foreach (var item in items)
        {
            var state = ItemStates.SingleOrDefault(x =>
                x.StockKeepingUnitId == item.StockKeepingUnitId);
            if (state is null)
                ItemStates.Add(InventoryCountItemViewState.FromItem(item, IsDraft));
            else
                state.Update(item, IsDraft);
        }
    }

    private async void OnEditQuantityTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is InventoryCountItemViewState item)
            await BeginQuantityEditAsync(item, InventoryCountPageMode.EditingExisting);
    }

    private async void OnRemoveItemTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is InventoryCountItemViewState item)
            await RemoveItemAsync(item);
    }

    private async void OnSaveQuantityClicked(object? sender, EventArgs e)
    {
        if (sender is Button
            {
                CommandParameter: InventoryCountItemViewState item
            })
            await SaveQuantityAsync(item);
    }

    private async void OnCancelQuantityClicked(object? sender, EventArgs e)
    {
        if (sender is Button
            {
                CommandParameter: InventoryCountItemViewState item
            })
            await CancelQuantityEditAsync(item);
    }

    private async Task BeginQuantityEditAsync(
        InventoryCountItemViewState item,
        InventoryCountPageMode mode)
    {
        if (_busy || _mode is not (InventoryCountPageMode.Scanning or InventoryCountPageMode.Searching))
            return;

        CameraScannerView.Stop();
        _editingItem?.EndEditing();
        _editingItem = item;
        _pendingQuantityRequestId = null;
        SetMode(mode);
        AccentItem(item.StockKeepingUnitId, null);
        item.BeginEditing();
        InstructionLabel.Text = item.IsPending
            ? "Введите итоговое количество в новой карточке товара."
            : "Введите итоговое количество в карточке товара.";
        await FocusQuantityEntryAsync(item);
    }

    private async Task SaveQuantityAsync(InventoryCountItemViewState item)
    {
        if (_busy || !ReferenceEquals(_editingItem, item))
            return;
        if (!TryReadQuantity(item, out var quantity))
            return;

        _pendingQuantityRequestId ??= Guid.NewGuid();
        SetBusy(true);
        try
        {
            MobileInventoryCountDetailsResponse details;
            if (item.IsPending)
            {
                details = await _apiClient.SetInventoryCountSkuQuantityAsync(
                    _details.Count.Id,
                    item.StockKeepingUnitId,
                    quantity,
                    _pendingQuantityRequestId.Value);
            }
            else if (item.ItemId is Guid itemId)
            {
                details = await _apiClient.SetInventoryCountItemQuantityAsync(
                    _details.Count.Id,
                    itemId,
                    quantity,
                    _pendingQuantityRequestId.Value);
            }
            else
            {
                throw new InvalidOperationException("Строка инвентаризации не содержит идентификатор.");
            }

            _pendingQuantityRequestId = null;
            await HideQuantityKeyboardAsync(item);
            item.EndEditing();
            _editingItem = null;
            SetMode(InventoryCountPageMode.Scanning);
            ApplyDetails(details);
            AccentItem(item.StockKeepingUnitId, null);
            await ReturnToScanningAsync();
        }
        catch (MobileApiException exception)
        {
            _pendingQuantityRequestId = null;
            ErrorLabel.Text = exception.Message;
        }
        catch (HttpRequestException)
        {
            ErrorLabel.Text = "Ответ сервера не получен. Повторите сохранение.";
        }
        finally
        {
            SetBusy(false);
            await UpdateCameraAsync();
        }
    }

    private async Task CancelQuantityEditAsync(InventoryCountItemViewState item)
    {
        if (_busy || !ReferenceEquals(_editingItem, item))
            return;

        _pendingQuantityRequestId = null;
        await HideQuantityKeyboardAsync(item);
        item.EndEditing();
        if (item.IsPending)
            ItemStates.Remove(item);
        _editingItem = null;
        await ReturnToScanningAsync();
    }

    private static bool TryReadQuantity(
        InventoryCountItemViewState item,
        out double quantity)
    {
        var value = item.QuantityText.Trim().Replace(',', '.');
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out quantity)
            && double.IsFinite(quantity)
            && quantity >= 0)
            return true;

        item.MarkQuantityInvalid();
        return false;
    }

    private async Task RemoveItemAsync(InventoryCountItemViewState item)
    {
        if (_busy
            || _mode != InventoryCountPageMode.Scanning
            || item.IsExpected
            || item.ItemId is not Guid itemId)
            return;
        if (_pendingRemoveItemId is not null && _pendingRemoveItemId != itemId)
        {
            ErrorLabel.Text = "Сначала повторите удаление предыдущего товара.";
            return;
        }
        if (_pendingRemoveRequestId is null
            && !await DisplayAlertAsync(
                "Удалить товар",
                $"Удалить ошибочно добавленный товар «{item.SkuName}»?",
                "Удалить",
                "Отмена"))
            return;

        _pendingRemoveItemId = itemId;
        _pendingRemoveRequestId ??= Guid.NewGuid();
        SetBusy(true);
        try
        {
            var details = await _apiClient.RemoveInventoryCountItemAsync(
                _details.Count.Id,
                itemId,
                _pendingRemoveRequestId.Value);
            _pendingRemoveRequestId = null;
            _pendingRemoveItemId = null;
            ApplyDetails(details);
        }
        catch (MobileApiException exception)
        {
            _pendingRemoveRequestId = null;
            _pendingRemoveItemId = null;
            ErrorLabel.Text = exception.Message;
        }
        catch (HttpRequestException)
        {
            ErrorLabel.Text = "Ответ сервера не получен. Повторите удаление этого же товара.";
        }
        finally
        {
            SetBusy(false);
            await UpdateCameraAsync();
        }
    }

    private void AccentItem(Guid stockKeepingUnitId, string? text)
    {
        foreach (var item in ItemStates)
            item.SetAccent(item.StockKeepingUnitId == stockKeepingUnitId, text);
    }

    private void ShowItem(Guid stockKeepingUnitId)
    {
        var item = ItemStates.SingleOrDefault(x => x.StockKeepingUnitId == stockKeepingUnitId);
        if (item is not null)
            ShowItem(item);
    }

    private void ShowItem(InventoryCountItemViewState item) =>
        ItemsCollectionView.ScrollTo(item, position: ScrollToPosition.MakeVisible, animate: false);

    private async void OnQuantityEntryLoaded(object? sender, EventArgs e)
    {
        if (sender is not Entry
            {
                BindingContext: InventoryCountItemViewState item
            } entry)
            return;

        _quantityEntries[item.StockKeepingUnitId] = entry;
        if (item.IsEditing)
            await FocusQuantityEntryAsync(item);
    }

    private void OnQuantityEntryUnloaded(object? sender, EventArgs e)
    {
        if (sender is not Entry
            {
                BindingContext: InventoryCountItemViewState item
            } entry)
            return;
        if (_quantityEntries.TryGetValue(item.StockKeepingUnitId, out var registered)
            && ReferenceEquals(registered, entry))
            _quantityEntries.Remove(item.StockKeepingUnitId);
    }

    private async Task FocusQuantityEntryAsync(InventoryCountItemViewState item)
    {
        await Task.Yield();
        if (!_quantityEntries.TryGetValue(item.StockKeepingUnitId, out var entry))
            return;

        _activeQuantityEntry = entry;
        entry.Focus();
        entry.CursorPosition = entry.Text?.Length ?? 0;
        entry.SelectionLength = 0;
    }

    private async Task HideQuantityKeyboardAsync(InventoryCountItemViewState item)
    {
        if (!_quantityEntries.TryGetValue(item.StockKeepingUnitId, out var entry))
            return;

        entry.Unfocus();
        await entry.HideSoftInputAsync(CancellationToken.None);
        if (ReferenceEquals(_activeQuantityEntry, entry))
            _activeQuantityEntry = null;
    }

    private void CloseSearch()
    {
        _searchVersion++;
        SetSearchBusy(false);
        SkuSearchEntry.Unfocus();
        SkuSearchEntry.Text = string.Empty;
        SkuSearchResults.Children.Clear();
    }

    private async Task ReturnToScanningAsync()
    {
        SetMode(InventoryCountPageMode.Scanning);
        StepLabel.Text = "Пересчёт товара";
        InstructionLabel.Text = "Отсканируйте товар. Каждый принятый скан добавляет одну единицу.";
        ErrorLabel.Text = string.Empty;
        await UpdateCameraAsync();
    }

    private void SetMode(InventoryCountPageMode mode)
    {
        _mode = mode;
        SkuSearchPanel.IsVisible = mode == InventoryCountPageMode.Searching;
        SkuSearchPrompt.IsVisible = mode == InventoryCountPageMode.Scanning;
        RefreshActionAvailability();
    }

    private async Task UpdateCameraAsync()
    {
        if (_scanner.ActiveSource == BarcodeScanSource.Camera && IsScanExpected)
            await CameraScannerView.StartAsync();
        else
            CameraScannerView.Stop();
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        ProgressIndicator.IsVisible = busy;
        ProgressIndicator.IsRunning = busy;
        RefreshActionAvailability();
    }

    private void RefreshActionAvailability()
    {
        var canRunDocumentAction = !_busy
            && IsDraft
            && _mode == InventoryCountPageMode.Scanning;
        PostButton.IsEnabled = canRunDocumentAction
            && _details.Items.All(x => x.CountedQuantity.HasValue);
        DeleteDraftButton.IsEnabled = canRunDocumentAction;
    }

    private void SetSearchBusy(bool busy)
    {
        SkuSearchIndicator.IsVisible = busy;
        SkuSearchIndicator.IsRunning = busy;
    }

    private void OnNonScanControlLoaded(object? sender, EventArgs e)
    {
        if (sender is VisualElement element)
            DisableAndroidFocus(element);
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

    private enum InventoryCountPageMode
    {
        Scanning,
        Searching,
        EditingExisting,
        EditingNew
    }
}

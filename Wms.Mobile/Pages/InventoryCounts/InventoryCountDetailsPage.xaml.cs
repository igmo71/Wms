using System.Collections.ObjectModel;
using System.Globalization;
using Wms.Contracts.Mobile.V1;
using Wms.Mobile.Scanning;
using Wms.Mobile.Services;

namespace Wms.Mobile;

public partial class InventoryCountDetailsPage : ContentPage
{
    private const int VisibleSearchResultCount = 10;

    private readonly MobileInventoryCountClient _inventoryCountClient;
    private readonly IOperationalBarcodeScanner _scanner;
    private MobileInventoryCountDetailsResponse _details;
    private int _searchVersion;
    private InventoryCountPageMode _mode = InventoryCountPageMode.Scanning;
    private InventoryCountItemViewState? _editingItem;
    private bool _editingExpirationConfirmed;
    private bool _isVisible;
    private bool _scannerSubscribed;
    private bool _busy;
    private MobileIncrementInventoryCountSkuRequest? _pendingScan;
    private Guid? _pendingQuantityRequestId;
    private decimal _pendingQuantity;
    private Guid? _pendingPostRequestId;
    private Guid? _pendingDeleteRequestId;
    private Guid? _pendingRemoveRequestId;
    private Guid? _pendingRemoveItemId;

    public InventoryCountDetailsPage(
        MobileInventoryCountClient inventoryCountClient,
        IOperationalBarcodeScanner scanner,
        MobileInventoryCountDetailsResponse details)
    {
        InitializeComponent();
        ItemStates.CollectionChanged += (_, _) =>
            EmptyItemsLabel.IsVisible = ItemStates.Count == 0;
        _inventoryCountClient = inventoryCountClient;
        _scanner = scanner;
        _details = details;
        CameraScannerView.Configure(scanner);
        ApplyDetails(details);
    }

    public ObservableCollection<InventoryCountItemViewState> ItemStates { get; } = [];

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _isVisible = true;
        if (!_scannerSubscribed)
        {
            _scanner.ScanReceived += OnScanReceived;
            _scannerSubscribed = true;
        }
        await UpdateCameraAsync();
    }

    protected override void OnDisappearing()
    {
        _isVisible = false;
        _searchVersion++;
        SkuSearchEntry.Unfocus();
        CameraScannerView.Stop();
        if (_scannerSubscribed)
        {
            _scanner.ScanReceived -= OnScanReceived;
            _scannerSubscribed = false;
        }
        base.OnDisappearing();
    }

    private bool IsDraft => _details.Count.Status == MobileInventoryCountStatus.Draft;
    private bool HasPendingCommand => _pendingScan is not null
        || _pendingQuantityRequestId is not null
        || _pendingPostRequestId is not null
        || _pendingDeleteRequestId is not null
        || _pendingRemoveRequestId is not null;
    private bool CanStartNewAction => IsDraft
        && !_busy
        && _mode == InventoryCountPageMode.Scanning
        && !HasPendingCommand;
    private bool IsScanExpected => IsDraft
        && !_busy
        && _mode == InventoryCountPageMode.Scanning
        && (!HasPendingCommand || _pendingScan is not null);

    private void OnScanReceived(object? sender, BarcodeScanEvent scanEvent) =>
        MainThread.BeginInvokeOnMainThread(async () => await IncrementScanAsync(scanEvent.Value));

    private async Task IncrementScanAsync(string barcode)
    {
        if (!IsScanExpected)
            return;
        if (_pendingScan is { } pending && pending.Barcode != barcode)
        {
            ErrorLabel.Text = "Повторите предыдущий штрихкод: ответ сервера не был получен.";
            return;
        }

        _pendingScan ??= new(Guid.NewGuid(), barcode, null, null);
        SetBusy(true);
        ErrorLabel.Text = string.Empty;
        try
        {
            var response = await _inventoryCountClient.IncrementSkuAsync(
                _details.Count.Id, _pendingScan);
            if (response.Confirmation is { } confirmation)
            {
                _pendingScan = null;
                ApplyDetails(response.Details);
                CameraScannerView.Stop();
                if (!IsDraft || !_isVisible)
                    return;
                var (confirmed, date) = await ChooseExpirationAsync(confirmation.SkuId, confirmation.SkuName);
                if (!confirmed || !_isVisible)
                    return;
                _pendingScan = new(Guid.NewGuid(), barcode, date, confirmation.SkuId);
                response = await _inventoryCountClient.IncrementSkuAsync(_details.Count.Id, _pendingScan);
            }
            _pendingScan = null;
            ApplyDetails(response.Details);
            if (response.Item is { } item)
            {
                AccentItem(item.Id, "+1");
                InstructionLabel.Text = "Принято +1. Сканируйте следующий товар.";
            }
        }
        catch (MobileApiException exception)
        {
            _pendingScan = null;
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
        if (!CanStartNewAction)
            return;

        SetMode(InventoryCountPageMode.Searching);
        CameraScannerView.Stop();
        StepLabel.Text = "Ручной выбор товара";
        InstructionLabel.Text = "Введите наименование, код или штрихкод.";
    }

    private async void OnCancelSkuSearchTapped(object? sender, TappedEventArgs e)
    {
        await ClearSearchAsync();
        ReturnToScanning();
        await UpdateCameraAsync();
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
            var results = await _inventoryCountClient.SearchSkusAsync(
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
        if (_busy || _mode != InventoryCountPageMode.Searching)
            return;
        SetBusy(true);
        var requiresExpiration = sku.RequiresExpiration || ItemStates.Any(x =>
            x.StockKeepingUnitId == sku.Id && x.ExpirationDate.HasValue);
        DateOnly? date = null;
        try
        {
            await ClearSearchAsync();
            if (requiresExpiration)
            {
                var (confirmed, chosenDate) = await ChooseExpirationAsync(sku.Id, sku.Name);
                if (!confirmed || !_isVisible)
                {
                    ReturnToScanning();
                    return;
                }
                date = chosenDate;
            }
        }
        finally
        {
            SetBusy(false);
            await UpdateCameraAsync();
        }
        var item = ItemStates.SingleOrDefault(x => x.StockKeepingUnitId == sku.Id && x.ExpirationDate == date);
        if (item is null)
        {
            item = InventoryCountItemViewState.FromPendingSku(sku, date, requiresExpiration);
            ItemStates.Add(item);
            SortItemStates();
        }
        BeginQuantityEdit(item, requiresExpiration);
    }

    private async Task<(bool Confirmed, DateOnly? Date)> ChooseExpirationAsync(Guid skuId, string skuName)
    {
        const string enterDate = "Ввести дату";
        const string unknown = "Срок неизвестен";
        var knownDates = _details.Items.Where(x => x.StockKeepingUnitId == skuId)
            .Select(x => x.ExpirationDate).OfType<DateOnly>().Distinct().Order()
            .ToDictionary(x => x.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture));
        var choice = await DisplayActionSheetAsync($"Срок годности · {skuName}", "Отмена", null,
            [.. knownDates.Keys, enterDate, unknown]);
        if (choice == unknown)
            return (true, null);
        if (choice is not null && knownDates.TryGetValue(choice, out var knownDate))
            return (true, knownDate);
        if (choice != enterDate)
            return (false, null);

        var message = "Введите дату с маркировки, например 31.12.2026.";
        while (true)
        {
            var input = await DisplayPromptAsync("Срок годности", message, "Подтвердить", "Отмена",
                placeholder: "дд.мм.гггг", maxLength: 10);
            if (input is null)
                return (false, null);
            if (DateOnly.TryParseExact(input.Trim(), "dd.MM.yyyy", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var parsed))
                return (true, parsed);
            message = "Укажите действительную дату в формате дд.мм.гггг.";
        }
    }

    private async void OnPostClicked(object? sender, EventArgs e)
    {
        if (_busy
            || _mode != InventoryCountPageMode.Scanning
            || (HasPendingCommand && _pendingPostRequestId is null))
            return;
        if (_details.Items.Any(x => x.CountedQuantity is null))
        {
            ErrorLabel.Text = "Сначала пересчитайте все ожидаемые позиции.";
            return;
        }
        if (_pendingPostRequestId is null)
        {
            SetBusy(true);
            var confirmed = await DisplayAlertAsync(
                "Провести инвентаризацию",
                "Остатки ячейки будут исправлены по фактическому количеству.",
                "Провести",
                "Отмена");
            if (!confirmed)
            {
                SetBusy(false);
                await UpdateCameraAsync();
                return;
            }
        }
        else
        {
            SetBusy(true);
        }

        _pendingPostRequestId ??= Guid.NewGuid();
        try
        {
            _details = await _inventoryCountClient.PostAsync(
                _details.Count.Id,
                _pendingPostRequestId.Value);
            _pendingPostRequestId = null;
            if (_isVisible)
            {
                await DisplayAlertAsync("Готово", "Инвентаризация проведена, ячейка освобождена.", "ОК");
                if (_isVisible)
                    await Navigation.PopAsync();
            }
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
            await UpdateCameraAsync();
        }
    }

    private async void OnDeleteDraftClicked(object? sender, EventArgs e)
    {
        if (_busy
            || _mode != InventoryCountPageMode.Scanning
            || (HasPendingCommand && _pendingDeleteRequestId is null))
            return;
        if (_pendingDeleteRequestId is null)
        {
            SetBusy(true);
            var confirmed = await DisplayAlertAsync(
                "Удалить черновик",
                "Введённые данные будут удалены, ячейка освобождена.",
                "Удалить",
                "Оставить");
            if (!confirmed)
            {
                SetBusy(false);
                await UpdateCameraAsync();
                return;
            }
        }
        else
        {
            SetBusy(true);
        }

        _pendingDeleteRequestId ??= Guid.NewGuid();
        try
        {
            await _inventoryCountClient.DeleteDraftAsync(
                _details.Count.Id,
                _pendingDeleteRequestId.Value);
            _pendingDeleteRequestId = null;
            if (_isVisible)
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
            await UpdateCameraAsync();
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
        var currentItemIds = items.Select(x => x.Id).ToHashSet();
        for (var index = ItemStates.Count - 1; index >= 0; index--)
        {
            var state = ItemStates[index];
            if (!state.IsPending && state.ItemId is Guid id && !currentItemIds.Contains(id))
                ItemStates.RemoveAt(index);
        }

        foreach (var item in items)
        {
            var state = ItemStates.SingleOrDefault(x =>
                x.ItemId == item.Id);
            if (state is null)
                ItemStates.Add(InventoryCountItemViewState.FromItem(item, IsDraft));
            else
                state.Update(item, IsDraft);
        }
        SortItemStates();
    }

    private void SortItemStates()
    {
        var sorted = ItemStates
            .OrderBy(x => x.SkuName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(x => x.StockKeepingUnitId).ThenBy(x => x.ExpirationDate).ToArray();
        for (var index = 0; index < sorted.Length; index++)
        {
            var currentIndex = ItemStates.IndexOf(sorted[index]);
            if (currentIndex != index)
                ItemStates.Move(currentIndex, index);
        }
    }

    private void OnEditQuantityTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is InventoryCountItemViewState item)
            BeginQuantityEdit(item);
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

    private void BeginQuantityEdit(InventoryCountItemViewState item, bool expirationConfirmed = false)
    {
        if (_busy
            || HasPendingCommand
            || _mode is not (InventoryCountPageMode.Scanning or InventoryCountPageMode.Searching))
            return;

        CameraScannerView.Stop();
        _editingItem = item;
        _editingExpirationConfirmed = expirationConfirmed;
        SetMode(InventoryCountPageMode.Editing);
        AccentItem(item.ItemId, null);
        item.BeginEditing();
        InstructionLabel.Text = item.IsPending
            ? "Введите итоговое количество в новой карточке товара."
            : "Введите итоговое количество в карточке товара.";
    }

    private async Task SaveQuantityAsync(InventoryCountItemViewState item)
    {
        if (_busy || !ReferenceEquals(_editingItem, item))
            return;
        var quantity = _pendingQuantity;
        if (_pendingQuantityRequestId is null && !TryReadQuantity(item, out quantity))
            return;

        SetBusy(true);
        item.SetQuantityLocked(true);
        try
        {
            var confirmExpiration = quantity > 0 && item.RequiresExpiration;
            if (_pendingQuantityRequestId is null && confirmExpiration && !_editingExpirationConfirmed
                && !await DisplayAlertAsync("Срок годности", item.ExpirationDate is DateOnly date
                    ? $"На товаре указан срок {date:dd.MM.yyyy}?"
                    : "Подтвердите, что срок на товаре неизвестен.", "Подтвердить", "Отмена"))
                return;
            _pendingQuantity = quantity;
            _pendingQuantityRequestId ??= Guid.NewGuid();
            MobileInventoryCountDetailsResponse details;
            if (item.IsPending)
            {
                details = await _inventoryCountClient.SetSkuQuantityAsync(
                    _details.Count.Id,
                    item.StockKeepingUnitId,
                    quantity,
                    item.ExpirationDate,
                    confirmExpiration,
                    _pendingQuantityRequestId.Value);
            }
            else if (item.ItemId is Guid itemId)
            {
                details = await _inventoryCountClient.SetItemQuantityAsync(
                    _details.Count.Id,
                    itemId,
                    quantity,
                    confirmExpiration,
                    _pendingQuantityRequestId.Value);
            }
            else
            {
                throw new InvalidOperationException("Строка инвентаризации не содержит идентификатор.");
            }

            _pendingQuantityRequestId = null;
            item.EndEditing();
            _editingItem = null;
            if (item.IsPending)
                ItemStates.Remove(item);
            ApplyDetails(details);
            var updated = details.Items.SingleOrDefault(x =>
                x.StockKeepingUnitId == item.StockKeepingUnitId && x.ExpirationDate == item.ExpirationDate);
            AccentItem(updated?.Id, null);
            ReturnToScanning();
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
            item.SetQuantityLocked(_pendingQuantityRequestId is not null);
            SetBusy(false);
            await UpdateCameraAsync();
        }
    }

    private async Task CancelQuantityEditAsync(InventoryCountItemViewState item)
    {
        if (_busy || !ReferenceEquals(_editingItem, item))
            return;
        if (_pendingQuantityRequestId is not null)
        {
            ErrorLabel.Text = "Сначала повторите сохранение количества.";
            return;
        }

        item.EndEditing();
        if (item.IsPending)
            ItemStates.Remove(item);
        _editingItem = null;
        ReturnToScanning();
        await UpdateCameraAsync();
    }

    private static bool TryReadQuantity(
        InventoryCountItemViewState item,
        out decimal quantity)
    {
        var value = item.QuantityText.Trim().Replace(',', '.');
        if (decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out quantity)
            && WarehouseQuantityInput.IsSupported(quantity)
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
        if (HasPendingCommand && _pendingRemoveRequestId is null)
        {
            ErrorLabel.Text = "Сначала повторите незавершённую операцию.";
            return;
        }
        if (_pendingRemoveItemId is not null && _pendingRemoveItemId != itemId)
        {
            ErrorLabel.Text = "Сначала повторите удаление предыдущего товара.";
            return;
        }
        if (_pendingRemoveRequestId is null)
        {
            SetBusy(true);
            var confirmed = await DisplayAlertAsync(
                "Удалить товар",
                $"Удалить ошибочно добавленный товар «{item.SkuName}»?",
                "Удалить",
                "Отмена");
            if (!confirmed)
            {
                SetBusy(false);
                await UpdateCameraAsync();
                return;
            }
        }
        else
        {
            SetBusy(true);
        }

        _pendingRemoveItemId = itemId;
        _pendingRemoveRequestId ??= Guid.NewGuid();
        try
        {
            var details = await _inventoryCountClient.RemoveItemAsync(
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

    private void AccentItem(Guid? itemId, string? text)
    {
        foreach (var item in ItemStates)
            item.SetAccent(item.ItemId == itemId, text);
    }

    private async Task ClearSearchAsync()
    {
        _searchVersion++;
        SetSearchBusy(false);
        SkuSearchEntry.Unfocus();
        await SkuSearchEntry.HideSoftInputAsync(CancellationToken.None);
        SkuSearchEntry.Text = string.Empty;
        SkuSearchResults.Children.Clear();
    }

    private void ReturnToScanning()
    {
        SetMode(InventoryCountPageMode.Scanning);
        StepLabel.Text = "Пересчёт товара";
        InstructionLabel.Text = "Отсканируйте товар. При необходимости подтвердите срок.";
        ErrorLabel.Text = string.Empty;
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
        if (_isVisible
            && _scanner.ActiveSource == BarcodeScanSource.Camera
            && IsScanExpected)
            await CameraScannerView.StartAsync();
        else
            CameraScannerView.Stop();
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        ProgressIndicator.Opacity = busy ? 1 : 0;
        RefreshActionAvailability();
    }

    private void RefreshActionAvailability()
    {
        var canRunDocumentAction = !_busy
            && IsDraft
            && _mode == InventoryCountPageMode.Scanning;
        PostButton.IsEnabled = canRunDocumentAction
            && (!HasPendingCommand || _pendingPostRequestId is not null)
            && _details.Items.All(x => x.CountedQuantity.HasValue);
        DeleteDraftButton.IsEnabled = canRunDocumentAction
            && (!HasPendingCommand || _pendingDeleteRequestId is not null);
        SkuSearchPrompt.IsEnabled = CanStartNewAction;
        foreach (var item in ItemStates)
            item.SetActionsEnabled(CanStartNewAction);
    }

    private void SetSearchBusy(bool busy)
    {
        SkuSearchIndicator.Opacity = busy ? 1 : 0;
    }

    private void OnNonScanControlLoaded(object? sender, EventArgs e)
    {
        if (sender is VisualElement element)
            AndroidFocus.Suppress(element);
    }

    private enum InventoryCountPageMode
    {
        Scanning,
        Searching,
        Editing
    }
}

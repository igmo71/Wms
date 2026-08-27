using System.Globalization;
using Wms.Contracts.Mobile.V1;
using Wms.Mobile.Scanning;
using Wms.Mobile.Services;

namespace Wms.Mobile;

public partial class InventoryCountDetailsPage : ContentPage
{
    private readonly MobileApiClient _apiClient;
    private readonly IOperationalBarcodeScanner _scanner;
    private MobileInventoryCountDetailsResponse _details;
    private readonly Dictionary<Guid, Border> _itemCards = [];
    private CancellationTokenSource? _searchCancellation;
    private bool _scannerSubscribed;
    private bool _busy;
    private Guid? _pendingScanRequestId;
    private string? _pendingBarcode;
    private Guid? _pendingQuantityRequestId;
    private Guid? _pendingPostRequestId;
    private Guid? _pendingDeleteRequestId;
    private Guid? _pendingRemoveRequestId;
    private Guid? _pendingRemoveItemId;
    private Guid? _editingItemId;
    private MobileInventoryCountSkuSearchResponse? _pendingNewSku;
    private Entry? _activeQuantityEntry;
    private Border? _pendingNewSkuCard;
    private Border? _keyboardScrollCard;
    private int _keyboardScrollVersion;
    private Guid? _accentedItemId;
    private string? _accentText;

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
        _searchCancellation?.Cancel();
        SkuSearchEntry.Unfocus();
        _activeQuantityEntry?.Unfocus();
        ResetKeyboardScroll();
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
        && !SkuSearchPanel.IsVisible
        && _editingItemId is null
        && _pendingNewSku is null;

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
            _accentedItemId = response.Item.Id;
            _accentText = "+1";
            ApplyDetails(response.Details);
            InstructionLabel.Text = "Принято +1. Сканируйте следующий товар.";
            await ScrollToItemAsync(response.Item.Id, ScrollToPosition.Center);
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
        SkuSearchPrompt.IsVisible = false;
        SkuSearchPanel.IsVisible = true;
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
        _searchCancellation?.Cancel();
        _searchCancellation = null;
        SkuSearchResults.Children.Clear();
        var query = e.NewTextValue?.Trim() ?? string.Empty;
        if (query.Length < 2)
        {
            SkuSearchStatusLabel.Text = "Введите не менее двух символов.";
            return;
        }

        var cancellation = new CancellationTokenSource();
        _searchCancellation = cancellation;
        try
        {
            await Task.Delay(300, cancellation.Token);
            SetSearchBusy(true);
            var results = await _apiClient.SearchInventoryCountSkusAsync(
                _details.Count.Id,
                query,
                CancellationToken.None);
            if (!cancellation.IsCancellationRequested && ReferenceEquals(_searchCancellation, cancellation))
                ShowSearchResults(results);
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

    private void ShowSearchResults(IReadOnlyList<MobileInventoryCountSkuSearchResponse> results)
    {
        SkuSearchResults.Children.Clear();
        SkuSearchStatusLabel.Text = results.Count == 10
            ? "Найдено: 10. Уточните запрос при необходимости."
            : $"Найдено: {results.Count}.";
        foreach (var sku in results)
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
        var item = _details.Items.SingleOrDefault(x => x.StockKeepingUnitId == sku.Id);
        if (item is null)
            await BeginNewSkuEditAsync(sku);
        else
            await BeginInlineQuantityEditAsync(item);
    }

    private async void OnPostClicked(object? sender, EventArgs e)
    {
        if (_details.Items.Any(x => x.CountedQuantity is null))
        {
            ErrorLabel.Text = "Сначала пересчитайте все ожидаемые позиции.";
            return;
        }
        if (_pendingPostRequestId is null
            && !await DisplayAlertAsync("Провести инвентаризацию", "Остатки ячейки будут исправлены по фактическому количеству.", "Провести", "Отмена"))
            return;

        _pendingPostRequestId ??= Guid.NewGuid();
        SetBusy(true);
        try
        {
            _details = await _apiClient.PostInventoryCountAsync(_details.Count.Id, _pendingPostRequestId.Value);
            _pendingPostRequestId = null;
            await DisplayAlertAsync("Готово", "Инвентаризация проведена, ячейка освобождена.", "ОК");
            await Navigation.PopAsync();
        }
        catch (MobileApiException exception)
        {
            _pendingPostRequestId = null;
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
        if (_pendingDeleteRequestId is null
            && !await DisplayAlertAsync("Удалить черновик", "Введённые данные будут удалены, ячейка освобождена.", "Удалить", "Оставить"))
            return;

        _pendingDeleteRequestId ??= Guid.NewGuid();
        SetBusy(true);
        try
        {
            await _apiClient.DeleteInventoryCountDraftAsync(_details.Count.Id, _pendingDeleteRequestId.Value);
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
        PostButton.IsEnabled = IsDraft
            && remaining == 0
            && !_busy
            && _editingItemId is null
            && _pendingNewSku is null;
        DeleteDraftButton.IsEnabled = IsDraft && !_busy;
        RenderItems();
    }

    private void RenderItems()
    {
        ItemsContainer.Children.Clear();
        _itemCards.Clear();
        _activeQuantityEntry = null;
        _pendingNewSkuCard = null;
        if (_details.Items.Count == 0 && _pendingNewSku is null)
        {
            ItemsContainer.Children.Add(new Label { Text = "Ячейка ожидается пустой. Отсканируйте найденный товар или проведите инвентаризацию.", FontSize = 16 });
            return;
        }

        foreach (var item in _details.Items)
        {
            var card = CreateItemCard(item, isPendingNewSku: false);
            _itemCards[item.Id] = card;
            ItemsContainer.Children.Add(card);
        }

        if (_pendingNewSku is not null)
        {
            var pendingItem = new MobileInventoryCountItemResponse(
                Guid.Empty,
                _pendingNewSku.Id,
                _pendingNewSku.Code,
                _pendingNewSku.Name,
                _pendingNewSku.UnitOfMeasure,
                0,
                null,
                null,
                false);
            _pendingNewSkuCard = CreateItemCard(pendingItem, isPendingNewSku: true);
            ItemsContainer.Children.Add(_pendingNewSkuCard);
        }
    }

    private Border CreateItemCard(
        MobileInventoryCountItemResponse item,
        bool isPendingNewSku)
    {
        var isAccented = isPendingNewSku
            || _accentedItemId == item.Id
            || _editingItemId == item.Id;
        var header = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = 8
        };
        header.Add(new Label
        {
            Text = item.SkuName,
            FontAttributes = FontAttributes.Bold,
            FontSize = 17,
            LineBreakMode = LineBreakMode.TailTruncation
        });
        if (!isPendingNewSku && _accentedItemId == item.Id && _accentText is not null)
        {
            var accentLabel = new Label
            {
                Text = _accentText,
                FontAttributes = FontAttributes.Bold,
                FontSize = 17,
                TextColor = AccentColor,
                VerticalTextAlignment = TextAlignment.Center
            };
            header.Add(accentLabel, 1);
        }

        var layout = new VerticalStackLayout { Spacing = 4 };
        layout.Children.Add(header);
        layout.Children.Add(new Label
        {
            Text = $"Ожид.: {Format(item.ExpectedQuantity)} · Факт: {Format(item.CountedQuantity)} · Разн.: {Format(item.DifferenceQuantity)} {item.UnitOfMeasure}",
            FontSize = 15,
            LineBreakMode = LineBreakMode.TailTruncation,
            MaxLines = 1
        });
        if (IsDraft)
        {
            if (isPendingNewSku)
                layout.Children.Add(CreateInlineQuantityEditor(
                    null,
                    entry => SaveNewSkuQuantityAsync(_pendingNewSku!, entry)));
            else if (_editingItemId == item.Id)
                layout.Children.Add(CreateInlineQuantityEditor(
                    item.CountedQuantity,
                    entry => SaveInlineQuantityAsync(item, entry)));
            else
                layout.Children.Add(CreateEditQuantityAction(item));

            if (!isPendingNewSku && !item.IsExpected && _editingItemId != item.Id)
            {
                var remove = new Label { Text = "Удалить ошибочную позицию", TextColor = Colors.IndianRed, FontSize = 15 };
                var removeTap = new TapGestureRecognizer();
                removeTap.Tapped += async (_, _) => await RemoveItemAsync(item);
                remove.GestureRecognizers.Add(removeTap);
                remove.Loaded += OnNonScanControlLoaded;
                layout.Children.Add(remove);
            }
        }

        return new Border
        {
            Padding = 13,
            Content = layout,
            Stroke = isAccented ? AccentColor : Colors.Gray,
            StrokeThickness = isAccented ? 3 : 1
        };
    }

    private View CreateEditQuantityAction(MobileInventoryCountItemResponse item)
    {
        var edit = new Label
        {
            Text = "Изменить количество",
            TextColor = Colors.DodgerBlue,
            FontSize = 15
        };
        var editTap = new TapGestureRecognizer();
        editTap.Tapped += async (_, _) => await BeginInlineQuantityEditAsync(item);
        edit.GestureRecognizers.Add(editTap);
        edit.Loaded += OnNonScanControlLoaded;
        return edit;
    }

    private View CreateInlineQuantityEditor(
        double? countedQuantity,
        Func<Entry, Task> save)
    {
        var quantityEntry = new Entry
        {
            Text = countedQuantity?.ToString("0.###", CultureInfo.InvariantCulture) ?? string.Empty,
            Keyboard = Keyboard.Numeric,
            FontSize = 20,
            Placeholder = "Количество"
        };
        _activeQuantityEntry = quantityEntry;
        var saveButton = new Button
        {
            Text = "✓",
            FontSize = 22,
            HeightRequest = 46,
            WidthRequest = 46,
            Padding = 0
        };
        saveButton.Clicked += async (_, _) => await save(quantityEntry);
        saveButton.Loaded += OnNonScanControlLoaded;
        var cancelButton = new Button
        {
            Text = "×",
            FontSize = 24,
            HeightRequest = 46,
            WidthRequest = 46,
            Padding = 0
        };
        cancelButton.Clicked += async (_, _) => await CancelInlineQuantityEditAsync(quantityEntry);
        cancelButton.Loaded += OnNonScanControlLoaded;

        var editor = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = 8
        };
        editor.Add(quantityEntry);
        editor.Add(saveButton, 1);
        editor.Add(cancelButton, 2);
        return editor;
    }

    private async Task BeginInlineQuantityEditAsync(MobileInventoryCountItemResponse item)
    {
        if (_busy)
            return;
        CameraScannerView.Stop();
        _pendingNewSku = null;
        _editingItemId = item.Id;
        _accentedItemId = item.Id;
        _accentText = null;
        _pendingQuantityRequestId = null;
        InstructionLabel.Text = "Введите итоговое количество в карточке товара.";
        RenderItems();
        await FocusEditorAndScrollAsync(_itemCards[item.Id]);
    }

    private async Task BeginNewSkuEditAsync(MobileInventoryCountSkuSearchResponse sku)
    {
        CameraScannerView.Stop();
        _editingItemId = null;
        _pendingNewSku = sku;
        _accentedItemId = null;
        _accentText = null;
        _pendingQuantityRequestId = null;
        InstructionLabel.Text = "Введите итоговое количество в новой карточке товара.";
        RenderItems();
        await FocusEditorAndScrollAsync(_pendingNewSkuCard!);
    }

    private async Task SaveInlineQuantityAsync(
        MobileInventoryCountItemResponse item,
        Entry quantityEntry)
    {
        if (_busy || _editingItemId != item.Id)
            return;
        var value = quantityEntry.Text?.Trim().Replace(',', '.');
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var quantity)
            || !double.IsFinite(quantity)
            || quantity < 0)
        {
            quantityEntry.TextColor = Colors.Red;
            return;
        }

        _pendingQuantityRequestId ??= Guid.NewGuid();
        SetBusy(true);
        try
        {
            var details = await _apiClient.SetInventoryCountItemQuantityAsync(
                _details.Count.Id,
                item.Id,
                quantity,
                _pendingQuantityRequestId.Value);
            _pendingQuantityRequestId = null;
            _editingItemId = null;
            _accentedItemId = item.Id;
            _accentText = null;
            quantityEntry.Unfocus();
            ResetKeyboardScroll();
            await quantityEntry.HideSoftInputAsync(CancellationToken.None);
            ApplyDetails(details);
            await ScrollToItemAsync(item.Id, ScrollToPosition.Center);
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

    private async Task SaveNewSkuQuantityAsync(
        MobileInventoryCountSkuSearchResponse sku,
        Entry quantityEntry)
    {
        if (_busy || _pendingNewSku?.Id != sku.Id)
            return;
        if (!TryReadQuantity(quantityEntry, out var quantity))
            return;

        _pendingQuantityRequestId ??= Guid.NewGuid();
        SetBusy(true);
        try
        {
            var details = await _apiClient.SetInventoryCountSkuQuantityAsync(
                _details.Count.Id,
                sku.Id,
                quantity,
                _pendingQuantityRequestId.Value);
            var item = details.Items.Single(x => x.StockKeepingUnitId == sku.Id);
            _pendingQuantityRequestId = null;
            _pendingNewSku = null;
            _accentedItemId = item.Id;
            _accentText = null;
            quantityEntry.Unfocus();
            ResetKeyboardScroll();
            await quantityEntry.HideSoftInputAsync(CancellationToken.None);
            ApplyDetails(details);
            await ScrollToItemAsync(item.Id, ScrollToPosition.Center);
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

    private async Task CancelInlineQuantityEditAsync(Entry quantityEntry)
    {
        if (_busy)
            return;
        _pendingQuantityRequestId = null;
        _editingItemId = null;
        _pendingNewSku = null;
        quantityEntry.Unfocus();
        ResetKeyboardScroll();
        await quantityEntry.HideSoftInputAsync(CancellationToken.None);
        RenderItems();
        await ReturnToScanningAsync();
    }

    private static bool TryReadQuantity(Entry quantityEntry, out double quantity)
    {
        var value = quantityEntry.Text?.Trim().Replace(',', '.');
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out quantity)
            && double.IsFinite(quantity)
            && quantity >= 0)
            return true;

        quantityEntry.TextColor = Colors.Red;
        return false;
    }

    private async Task FocusEditorAndScrollAsync(Border card)
    {
        _keyboardScrollCard = card;
        _keyboardScrollVersion++;
        KeyboardScrollSpacer.HeightRequest = 64;
        KeyboardScrollSpacer.IsVisible = true;
        await Task.Yield();
        _activeQuantityEntry?.Focus();
        await PositionEditorAboveKeyboardAsync(card);
    }

    private async void OnPageScrollViewSizeChanged(object? sender, EventArgs e)
    {
        var card = _keyboardScrollCard;
        if (card is null)
            return;

        var version = ++_keyboardScrollVersion;
        await Task.Delay(50);
        if (version == _keyboardScrollVersion
            && ReferenceEquals(card, _keyboardScrollCard))
            await PositionEditorAboveKeyboardAsync(card);
    }

    private async Task PositionEditorAboveKeyboardAsync(Border card)
    {
        if (card.Handler is null)
            return;

        await PageScrollView.ScrollToAsync(card, ScrollToPosition.End, false);
        await PageScrollView.ScrollToAsync(0, PageScrollView.ScrollY + 20, true);
    }

    private void ResetKeyboardScroll()
    {
        _keyboardScrollCard = null;
        _keyboardScrollVersion++;
        KeyboardScrollSpacer.HeightRequest = 0;
        KeyboardScrollSpacer.IsVisible = false;
    }

    private async Task ScrollToItemAsync(Guid itemId, ScrollToPosition position)
    {
        if (!_itemCards.TryGetValue(itemId, out var card))
            return;
        await Task.Yield();
        await PageScrollView.ScrollToAsync(card, position, true);
    }

    private async Task RemoveItemAsync(MobileInventoryCountItemResponse item)
    {
        if (_busy)
            return;
        if (_pendingRemoveItemId is not null && _pendingRemoveItemId != item.Id)
        {
            ErrorLabel.Text = "Сначала повторите удаление предыдущего товара.";
            return;
        }
        if (_pendingRemoveRequestId is null
            && !await DisplayAlertAsync("Удалить товар", $"Удалить ошибочно добавленный товар «{item.SkuName}»?", "Удалить", "Отмена"))
            return;

        _pendingRemoveItemId = item.Id;
        _pendingRemoveRequestId ??= Guid.NewGuid();
        SetBusy(true);
        try
        {
            _details = await _apiClient.RemoveInventoryCountItemAsync(
                _details.Count.Id,
                item.Id,
                _pendingRemoveRequestId.Value);
            _pendingRemoveRequestId = null;
            _pendingRemoveItemId = null;
            ApplyDetails(_details);
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

    private void CloseSearch()
    {
        _searchCancellation?.Cancel();
        _searchCancellation = null;
        SetSearchBusy(false);
        SkuSearchEntry.Unfocus();
        SkuSearchEntry.Text = string.Empty;
        SkuSearchResults.Children.Clear();
        SkuSearchPanel.IsVisible = false;
        SkuSearchPrompt.IsVisible = true;
    }

    private async Task ReturnToScanningAsync()
    {
        StepLabel.Text = "Пересчёт товара";
        InstructionLabel.Text = "Отсканируйте товар. Каждый принятый скан добавляет одну единицу.";
        ErrorLabel.Text = string.Empty;
        await UpdateCameraAsync();
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
        PostButton.IsEnabled = !busy
            && IsDraft
            && _editingItemId is null
            && _pendingNewSku is null
            && _details.Items.All(x => x.CountedQuantity.HasValue);
        DeleteDraftButton.IsEnabled = !busy && IsDraft;
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

    private static string Format(double? value) => value?.ToString("0.###") ?? "—";

    private static Color AccentColor =>
        Application.Current?.Resources.TryGetValue("Primary", out var value) == true
            && value is Color color
                ? color
                : Color.FromArgb("#512BD4");
}

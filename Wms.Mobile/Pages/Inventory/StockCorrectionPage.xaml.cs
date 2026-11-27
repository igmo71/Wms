using System.Globalization;
using Wms.Contracts.Mobile.V1;
using Wms.Mobile.Scanning;
using Wms.Mobile.Services;

namespace Wms.Mobile;

public partial class StockCorrectionPage : ContentPage
{
    private readonly MobileStockCorrectionClient _client;
    private readonly MobileReferenceDataClient _referenceData;
    private readonly MobileWarehouseSelection _warehouseSelection;
    private readonly IOperationalBarcodeScanner _scanner;
    private readonly BackButtonBehavior _backButton = new();
    private MobileStockCorrectionCellResponse? _cell;
    private MobileStockCorrectionGroup? _selected;
    private MobileCorrectCellStockRequest? _pending;
    private bool _scanSku;
    private bool _visible;
    private bool _busy;
    private bool _warehousesLoaded;
    private long _scanGeneration;

    public StockCorrectionPage(MobileStockCorrectionClient client, MobileReferenceDataClient referenceData,
        MobileWarehouseSelection warehouseSelection, IOperationalBarcodeScanner scanner)
    {
        InitializeComponent();
        _client = client;
        _referenceData = referenceData;
        _warehouseSelection = warehouseSelection;
        _scanner = scanner;
        Shell.SetBackButtonBehavior(this, _backButton);
        CameraScannerView.Configure(scanner);
    }

    private MobileWarehouseResponse? Warehouse => WarehousePicker.SelectedItem as MobileWarehouseResponse;
    private bool CanEdit => !_busy && _pending is null;
    private bool CanScan => _visible && CanEdit && Warehouse is not null && (_cell is null || _scanSku);

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _visible = true;
        _scanner.ScanReceived += OnScanReceived;
        if (!_warehousesLoaded)
        {
            SetBusy(true);
            try
            {
                var warehouses = await _referenceData.GetWarehousesAsync();
                WarehousePicker.ItemsSource = warehouses.ToList();
                _warehousesLoaded = true;
                var (index, warning) = _warehouseSelection.Resolve(warehouses);
                WarehousePicker.SelectedIndex = index;
                ErrorLabel.Text = warning ?? (warehouses.Count == 0 ? "Нет доступных складов." : string.Empty);
            }
            catch (MobileApiException exception) { ErrorLabel.Text = exception.Message; }
            catch (HttpRequestException) { ErrorLabel.Text = "Не удалось загрузить склады."; }
            finally { SetBusy(false); }
        }
        await UpdateCameraAsync();
    }

    protected override void OnDisappearing()
    {
        _visible = false;
        Interlocked.Increment(ref _scanGeneration);
        _scanner.ScanReceived -= OnScanReceived;
        CameraScannerView.Stop();
        base.OnDisappearing();
    }

    protected override bool OnBackButtonPressed()
    {
        if (CanEdit) return base.OnBackButtonPressed();
        if (!_busy) ErrorLabel.Text = "Повторите отправку, чтобы узнать результат корректировки.";
        return true;
    }

    private async void OnWarehouseChanged(object? sender, EventArgs e)
    {
        if (Warehouse is { } warehouse)
            _warehouseSelection.Select(warehouse.Id);
        if (_busy) return;
        await ChangeCellAsync();
    }

    private async void OnChangeCellClicked(object? sender, EventArgs e) => await ChangeCellAsync();

    private async Task ChangeCellAsync()
    {
        if (!CanEdit) return;
        Interlocked.Increment(ref _scanGeneration);
        SetCell(null);
        BarcodeEntry.Text = string.Empty;
        ErrorLabel.Text = string.Empty;
        StatusLabel.Text = string.Empty;
        await UpdateCameraAsync();
    }

    private async void OnScanSkuClicked(object? sender, EventArgs e)
    {
        if (!CanEdit || _cell is not { HoldReason: null }) return;
        Interlocked.Increment(ref _scanGeneration);
        _scanSku = !_scanSku;
        BarcodeEntry.Text = string.Empty;
        ErrorLabel.Text = string.Empty;
        SetGroup(null);
        await UpdateCameraAsync();
    }

    private void OnScanReceived(object? sender, BarcodeScanEvent scan)
    {
        var generation = Volatile.Read(ref _scanGeneration);
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            if (generation == Volatile.Read(ref _scanGeneration))
                await ReadBarcodeAsync(scan.Value);
        });
    }

    private async void OnReadBarcodeClicked(object? sender, EventArgs e) =>
        await ReadBarcodeAsync(BarcodeEntry.Text);

    private async Task ReadBarcodeAsync(string? barcode)
    {
        if (!CanScan || Warehouse is not { } warehouse || string.IsNullOrWhiteSpace(barcode)) return;

        SetBusy(true);
        ErrorLabel.Text = string.Empty;
        StatusLabel.Text = string.Empty;
        try
        {
            barcode = barcode.Trim();
            if (_scanSku && _cell is { } cell)
            {
                var sku = await _referenceData.ResolveSkuAsync(barcode);
                var (confirmed, date) = await ChooseExpirationAsync(sku);
                if (!confirmed) return;
                _scanSku = false;
                SetGroup(cell.Groups.FirstOrDefault(x => x.SkuId == sku.Id && x.ExpirationDate == date)
                    ?? new MobileStockCorrectionGroup(sku.Id, sku.Code, sku.Name, sku.UnitOfMeasure,
                        date, 0, 0, 0, 0, 0, true));
                await PageScroll.ScrollToAsync(EditPanel, ScrollToPosition.End, true);
            }
            else
            {
                SetCell(await _client.ReadCellAsync(warehouse.Id, barcode));
            }
            BarcodeEntry.Text = string.Empty;
        }
        catch (MobileApiException exception) { ErrorLabel.Text = exception.Message; }
        catch (HttpRequestException) { ErrorLabel.Text = "Не удалось получить сведения из WMS."; }
        finally
        {
            SetBusy(false);
            await UpdateCameraAsync();
        }
    }

    private async Task<(bool Confirmed, DateOnly? Date)> ChooseExpirationAsync(MobileSkuResponse sku)
    {
        const string useDate = "Указать дату";
        const string withoutDate = "Без даты / срок неизвестен";
        var choice = await DisplayActionSheetAsync(sku.Name, "Отмена", null, useDate, withoutDate);
        if (choice == withoutDate) return (true, null);
        if (choice != useDate) return (false, null);

        var input = await DisplayPromptAsync("Срок годности", "Дата на упаковке в формате ДД.ММ.ГГГГ",
            "Выбрать", "Отмена", placeholder: "ДД.ММ.ГГГГ", maxLength: 10);
        if (input is null) return (false, null);
        if (DateOnly.TryParseExact(input.Trim(), ["d.M.yyyy", "dd.MM.yyyy"], CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var date))
            return (true, date);
        ErrorLabel.Text = "Введите действительную дату в формате ДД.ММ.ГГГГ.";
        return (false, null);
    }

    private void SetCell(MobileStockCorrectionCellResponse? cell)
    {
        _cell = cell;
        _scanSku = false;
        AddressLabel.Text = cell is null ? string.Empty : $"{cell.Address} · {cell.Name}";
        HoldLabel.Text = cell?.HoldReason;
        GroupsPanel.Children.Clear();
        foreach (var group in cell?.Groups ?? [])
        {
            var row = new VerticalStackLayout { Spacing = 4 };
            row.Children.Add(new Label { Text = GroupTitle(group), FontSize = 16, FontAttributes = FontAttributes.Bold });
            row.Children.Add(new Label
            {
                Text = $"По учёту: {group.AccountingQuantity:0.###} · Свободно: {group.AvailableQuantity:0.###}\n"
                    + $"Уже отобрано: {group.ConfirmedPickQuantity:0.###} · В маршруте: {group.RouteReservedQuantity:0.###}\n"
                    + $"Заблокировано недостачей: {group.ShortageBlockedQuantity:0.###}"
            });
            var card = new Border { Padding = 12, Content = row };
            var tap = new TapGestureRecognizer();
            tap.Tapped += async (_, _) =>
            {
                if (!CanEdit || _cell is not { HoldReason: null }) return;
                Interlocked.Increment(ref _scanGeneration);
                _scanSku = false;
                ErrorLabel.Text = string.Empty;
                StatusLabel.Text = string.Empty;
                SetGroup(group);
                await UpdateCameraAsync();
                await PageScroll.ScrollToAsync(EditPanel, ScrollToPosition.End, true);
            };
            card.GestureRecognizers.Add(tap);
            GroupsPanel.Children.Add(card);
        }
        SetGroup(null);
    }

    private void SetGroup(MobileStockCorrectionGroup? group)
    {
        _selected = group;
        SelectedGroupLabel.Text = group is null ? string.Empty : GroupTitle(group);
        QuantityEntry.Text = string.Empty;
        QuantityErrorLabel.IsVisible = false;
        Render();
    }

    private static string GroupTitle(MobileStockCorrectionGroup group)
    {
        var sku = string.IsNullOrWhiteSpace(group.SkuName) ? group.SkuCode : group.SkuName;
        if (!string.IsNullOrWhiteSpace(group.SkuCode) && sku != group.SkuCode)
            sku = $"{sku} ({group.SkuCode})";
        var date = group.ExpirationDate?.ToString("dd.MM.yyyy") ?? "Без даты";
        var unit = string.IsNullOrWhiteSpace(group.Unit) ? "ед. неизвестна" : group.Unit;
        return $"{sku} · {date} · {unit}";
    }

    private async void OnAddClicked(object? sender, EventArgs e) => await ConfirmAsync(adding: true);
    private async void OnRemoveClicked(object? sender, EventArgs e) => await ConfirmAsync(adding: false);
    private async void OnRetryClicked(object? sender, EventArgs e) => await SendPendingAsync();

    private async Task ConfirmAsync(bool adding)
    {
        if (!CanEdit || _cell is not { HoldReason: null } cell || _selected is not { } group) return;
        var text = (QuantityEntry.Text ?? string.Empty).Trim().Replace(',', '.');
        if (!decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var quantity)
            || quantity <= 0 || !WarehouseQuantityInput.IsSupported(quantity))
        {
            QuantityErrorLabel.Text = "Введите положительное количество с точностью до трёх знаков.";
            QuantityErrorLabel.IsVisible = true;
            return;
        }
        if (!adding && quantity > group.AvailableQuantity)
        {
            QuantityErrorLabel.Text = $"Свободно для списания: {group.AvailableQuantity:0.###}.";
            QuantityErrorLabel.IsVisible = true;
            return;
        }

        SetBusy(true);
        QuantityErrorLabel.IsVisible = false;
        ErrorLabel.Text = string.Empty;
        try
        {
            var action = adding ? "Добавить" : "Списать";
            var confirm = await DisplayAlertAsync("Подтвердить корректировку",
                $"{cell.Address}\n{GroupTitle(group)}\n{action}: {quantity:0.###}", action, "Отмена");
            if (confirm)
                _pending = new MobileCorrectCellStockRequest(Guid.NewGuid(), cell.WarehouseId, cell.LocationId,
                    group.SkuId, group.ExpirationDate, adding ? quantity : -quantity);
        }
        finally { SetBusy(false); }
        await SendPendingAsync();
    }

    private async Task SendPendingAsync()
    {
        if (_busy || _pending is not { } pending || _cell is not { } cell) return;
        SetBusy(true);
        ErrorLabel.Text = string.Empty;
        try
        {
            try
            {
                await _client.PostAsync(pending);
                _pending = null;
                StatusLabel.Text = $"Проведено: {pending.QuantityChange:+0.###;-0.###} · {cell.Address}";
            }
            catch (MobileApiException exception)
            {
                _pending = null;
                ErrorLabel.Text = exception.Message;
            }
            catch (HttpRequestException)
            {
                ErrorLabel.Text = "Результат не подтверждён. Повторите отправку этой корректировки.";
                return;
            }

            try { SetCell(await _client.ReadCellAsync(cell.WarehouseId, cell.Code)); }
            catch (MobileApiException exception)
            {
                SetCell(null);
                ErrorLabel.Text = exception.Message;
            }
            catch (HttpRequestException)
            {
                SetCell(null);
                ErrorLabel.Text = "Не удалось обновить остатки. Отсканируйте ячейку ещё раз.";
            }
        }
        finally
        {
            SetBusy(false);
            await PageScroll.ScrollToAsync(0, 0, true);
            await UpdateCameraAsync();
        }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        if (busy)
        {
            Interlocked.Increment(ref _scanGeneration);
            CameraScannerView.Stop();
        }
        Render();
    }

    private void Render()
    {
        _backButton.IsEnabled = CanEdit;
        WarehousePicker.IsEnabled = CanEdit;
        CellPanel.IsVisible = _cell is not null;
        CellPanel.IsEnabled = CanEdit;
        EmptyLabel.IsVisible = _cell?.Groups.Count == 0;
        HoldLabel.IsVisible = _cell?.HoldReason is not null;
        ScanSkuButton.IsVisible = _cell is { HoldReason: null };
        ScanSkuButton.Text = _scanSku ? "Отменить сканирование" : "Сканировать товар";
        ScanPanel.IsVisible = CanScan;
        InstructionLabel.Text = Warehouse is null ? "Выберите склад."
            : _cell is null ? "Отсканируйте ячейку."
            : _scanSku ? "Отсканируйте товар для корректировки."
            : _cell.HoldReason is null ? "Выберите товар и срок в списке или отсканируйте товар." : string.Empty;
        InstructionLabel.IsVisible = !string.IsNullOrEmpty(InstructionLabel.Text);
        EditPanel.IsVisible = _selected is not null;
        EditPanel.IsEnabled = CanEdit;
        AddButton.IsEnabled = _selected?.CanAdd == true;
        RemoveButton.IsEnabled = _selected is { AvailableQuantity: > 0 };
        RetryButton.IsVisible = _pending is not null;
        RetryButton.IsEnabled = !_busy;
        ProgressIndicator.IsRunning = _busy;
        ProgressIndicator.IsVisible = _busy;
        StatusLabel.IsVisible = !string.IsNullOrEmpty(StatusLabel.Text);
        ErrorLabel.IsVisible = !string.IsNullOrEmpty(ErrorLabel.Text);
    }

    private async Task UpdateCameraAsync()
    {
        if (CanScan && _scanner.ActiveSource == BarcodeScanSource.Camera)
            await CameraScannerView.StartAsync();
        else
            CameraScannerView.Stop();
    }
}

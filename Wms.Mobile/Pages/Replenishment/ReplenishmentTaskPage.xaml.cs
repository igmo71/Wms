using System.Globalization;
using Wms.Contracts.Mobile.V1;
using Wms.Mobile.Scanning;
using Wms.Mobile.Services;

namespace Wms.Mobile;

public partial class ReplenishmentTaskPage : ContentPage
{
    private readonly MobileReplenishmentClient _client;
    private readonly MobileWarehouseSelection _selection;
    private readonly IOperationalBarcodeScanner _scanner;
    private readonly ReplenishmentTaskProcess _process;
    private MobileReplenishmentTaskResponse? _task;
    private Guid _taskId;
    private Guid _warehouseId;
    private ScanTarget _scanTarget = ScanTarget.Source;
    private bool _visible;
    private bool _busy;
    private bool _cameraRequested;
    private long _scanGeneration;

    public ReplenishmentTaskPage(MobileReplenishmentClient client,
        MobileWarehouseSelection selection, IOperationalBarcodeScanner scanner,
        ReplenishmentTaskProcess process)
    {
        InitializeComponent();
        _client = client;
        _selection = selection;
        _scanner = scanner;
        _process = process;
        BindingContext = this;
        CameraScannerView.Configure(scanner);
    }

    public IReadOnlyList<ReplenishmentStockRow> StockRows { get; private set; } = [];
    public IReadOnlyList<ReplenishmentTransferRow> TransferRows { get; private set; } = [];
    private MobileReplenishmentSourceCycleResponse? ActiveCycle =>
        _task?.Cycles.SingleOrDefault(x => x.CompletedAtUtc is null);
    private ReplenishmentGroupRow? SelectedGroup =>
        GroupPicker.SelectedItem as ReplenishmentGroupRow;
    private decimal Remaining => Math.Max(0, (_task?.OrderedQuantity ?? 0)
        - (_task?.TransferredQuantity ?? 0));
    private bool CanOperate => _task is not null &&
        (_task.AssignedTo is null || _task.AssignedTo == _selection.CurrentUserId);

    public void Show(Guid taskId, Guid warehouseId)
    {
        _taskId = taskId;
        _warehouseId = warehouseId;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _visible = true;
        _scanner.ScanReceived += OnScanReceived;
        CameraButton.IsVisible = _scanner.ActiveSource == BarcodeScanSource.Camera;
        await RefreshAsync();
        await UpdateCameraAsync();
    }

    protected override void OnDisappearing()
    {
        _visible = false;
        _cameraRequested = false;
        Interlocked.Increment(ref _scanGeneration);
        _scanner.ScanReceived -= OnScanReceived;
        CameraScannerView.Stop();
        base.OnDisappearing();
    }

    private async void OnRefreshing(object? sender, EventArgs e)
    {
        await RefreshAsync();
        TaskRefreshView.IsRefreshing = false;
    }

    private async Task RefreshAsync()
    {
        if (_busy || _taskId == Guid.Empty) return;
        SetBusy(true);
        try
        {
            _task = await _client.TaskAsync(_taskId, _warehouseId);
            Apply();
            if (!_process.HasPendingCommand) ErrorLabel.Text = string.Empty;
        }
        catch (MobileApiException exception) { ErrorLabel.Text = exception.Message; }
        catch (HttpRequestException) { ErrorLabel.Text = "Не удалось обновить задание Подпитки."; }
        finally { SetBusy(false); await UpdateCameraAsync(); }
    }

    private void Apply()
    {
        if (_task is null) return;
        var cycle = ActiveCycle;
        SkuLabel.Text = _task.SkuName ?? _task.SkuId.ToString();
        ProgressLabel.Text = $"Проведено {_task.TransferredQuantity:0.###} из "
            + $"{_task.OrderedQuantity:0.###}; осталось {Remaining:0.###}";
        StatusLabel.Text = cycle is not null
            ? $"Поддон снят: {cycle.SourceCode}. Требуется возврат или подтверждение пустой ячейки."
            : _task.Status == MobileReplenishmentTaskStatus.Completed
                ? "Задание завершено" : Remaining == 0
                    ? "Все количество проведено" : "Ожидается скан источника";
        RecommendationLabel.Text = $"Источник: {_task.RecommendedSourceCode ?? "нет доступного источника"}"
            + $" · Назначение: {_task.RecommendedDestinationCode ?? "выбрать по скану"}";
        StartPanel.IsVisible = cycle is null && Remaining > 0 && CanOperate;
        ActivePanel.IsVisible = cycle is not null;
        StartButton.IsEnabled = !_busy && !_process.HasPendingCommand && CanOperate;
        if (cycle is not null)
        {
            ActiveSourceLabel.Text = $"Фактический источник: {cycle.SourceCode}";
            ReturnRequiredLabel.Text = "Исходная ячейка удерживается до повторного скана.";
            var selectedGroup = SelectedGroup;
            var groups = _task.ActiveSourceStock
                .Where(x => x.SkuId == _task.SkuId && x.AvailableQuantity > 0)
                .OrderBy(x => x.ExpirationDate is null)
                .ThenBy(x => x.ExpirationDate)
                .Select(x => new ReplenishmentGroupRow(x.ExpirationDate,
                    x.AvailableQuantity)).ToArray();
            GroupPicker.ItemsSource = groups;
            var index = selectedGroup is null ? -1 : Array.FindIndex(groups,
                x => x.ExpirationDate == selectedGroup.ExpirationDate);
            GroupPicker.SelectedIndex = index >= 0 ? index : groups.Length > 0 ? 0 : -1;
            StockRows = _task.ActiveSourceStock.Select(x => new ReplenishmentStockRow(
                x.SkuName ?? x.SkuId.ToString(), x.SkuId == _task.SkuId,
                x.ExpirationDate, x.AccountingQuantity, x.BlockedQuantity)).ToArray();
            OnPropertyChanged(nameof(StockRows));
            if (string.IsNullOrWhiteSpace(QuantityEntry.Text) && SelectedGroup is { } group)
                QuantityEntry.Text = Math.Min(Remaining, group.Available)
                    .ToString("0.###", CultureInfo.CurrentCulture);
            if (string.IsNullOrWhiteSpace(DestinationEntry.Text))
                DestinationEntry.Placeholder = _task.RecommendedDestinationCode is { } destination
                    ? $"Сканируйте назначение, рекомендовано {destination}"
                    : "Сканируйте ячейку Комплектации";
            TransferButton.IsEnabled = !_busy && !_process.HasPendingCommand
                && CanOperate && Remaining > 0 && groups.Length > 0;
            ShortageButton.IsEnabled = TransferButton.IsEnabled;
            ReturnButton.IsEnabled = !_busy && !_process.HasPendingCommand && CanOperate;
            EmptyButton.IsEnabled = ReturnButton.IsEnabled;
            _scanTarget = Remaining == 0 || groups.Length == 0
                ? ScanTarget.Return : ScanTarget.Destination;
        }
        else
        {
            StockRows = [];
            OnPropertyChanged(nameof(StockRows));
            GroupPicker.ItemsSource = null;
            _scanTarget = ScanTarget.Source;
        }
        TransferRows = _task.Cycles.SelectMany(x => x.Transfers.Select(t =>
            new ReplenishmentTransferRow(x.SourceCode, t.DestinationCode,
                t.ExpirationDate, t.EffectiveQuantity))).ToArray();
        OnPropertyChanged(nameof(TransferRows));
    }

    private void OnGroupChanged(object? sender, EventArgs e)
    {
        if (SelectedGroup is { } group)
        {
            QuantityEntry.Text = Math.Min(Remaining, group.Available)
                .ToString("0.###", CultureInfo.CurrentCulture);
            ExpectedEntry.Text = QuantityEntry.Text;
        }
    }

    private void OnSourceFocused(object? sender, FocusEventArgs e) =>
        _scanTarget = ScanTarget.Source;
    private void OnDestinationFocused(object? sender, FocusEventArgs e) =>
        _scanTarget = ScanTarget.Destination;
    private void OnReturnFocused(object? sender, FocusEventArgs e) =>
        _scanTarget = ScanTarget.Return;

    private void OnScanReceived(object? sender, BarcodeScanEvent scan)
    {
        var generation = Volatile.Read(ref _scanGeneration);
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (!_visible || _busy || generation != Volatile.Read(ref _scanGeneration)) return;
            var entry = _scanTarget switch
            {
                ScanTarget.Destination => DestinationEntry,
                ScanTarget.Return => ReturnEntry,
                _ => SourceEntry
            };
            entry.Text = scan.Value;
        });
    }

    private async void OnCameraClicked(object? sender, EventArgs e)
    {
        if (_busy || _scanner.ActiveSource != BarcodeScanSource.Camera) return;
        _cameraRequested = !_cameraRequested;
        await UpdateCameraAsync();
    }

    private async Task UpdateCameraAsync()
    {
        CameraButton.Text = _cameraRequested ? "Закрыть камеру" : "Сканировать ячейку";
        if (_visible && !_busy && !_process.HasPendingCommand && _cameraRequested
            && _scanner.ActiveSource == BarcodeScanSource.Camera)
            await CameraScannerView.StartAsync();
        else CameraScannerView.Stop();
    }

    private async void OnStartClicked(object? sender, EventArgs e)
    {
        if (_busy || _process.HasPendingCommand || _task is null || ActiveCycle is not null
            || !CanOperate || string.IsNullOrWhiteSpace(SourceEntry.Text)) return;
        var code = SourceEntry.Text.Trim();
        if (!await DisplayAlertAsync("Снять поддон",
            $"Подтвердите источник {code} для SKU {SkuLabel.Text}.",
            "Подтвердить", "Отмена")) return;
        await RunCommandAsync(() => _process.StartSourceAsync(_taskId, _warehouseId, code));
    }

    private async void OnTransferClicked(object? sender, EventArgs e)
    {
        if (_busy || _process.HasPendingCommand || _task is null || !CanOperate
            || ActiveCycle is not { } cycle || SelectedGroup is not { } group
            || string.IsNullOrWhiteSpace(DestinationEntry.Text)) return;
        if (!TryQuantity(QuantityEntry.Text, out var quantity)
            || quantity > Remaining || quantity > group.Available)
        {
            ErrorLabel.Text = "Количество должно быть положительным и не больше остатка заказа и группы срока.";
            return;
        }
        var destination = DestinationEntry.Text.Trim();
        if (!await DisplayAlertAsync("Провести Подпитку",
            $"Перенести {quantity:0.###} из {cycle.SourceCode} в {destination}?",
            "Провести", "Отмена")) return;
        await RunCommandAsync(() => _process.TransferAsync(_taskId, _warehouseId,
            cycle.Id, destination, _task.SkuId, group.ExpirationDate, quantity));
    }

    private async void OnShortageClicked(object? sender, EventArgs e)
    {
        if (_busy || _process.HasPendingCommand || _task is null || !CanOperate
            || ActiveCycle is not { } cycle || SelectedGroup is not { } group) return;
        if (!TryQuantity(ExpectedEntry.Text, out var expected)
            || expected > Remaining || expected > group.Available
            || !decimal.TryParse(FoundEntry.Text, NumberStyles.Number,
                CultureInfo.CurrentCulture, out var found)
            || found < 0 || found >= expected)
        {
            ErrorLabel.Text = "Укажите ожидаемое количество и меньшее найденное количество выбранного срока.";
            return;
        }
        if (!await DisplayAlertAsync("Недостача",
            $"В {cycle.SourceCode} ожидалось {expected:0.###}, найдено {found:0.###}. Зафиксировать расхождение?",
            "Зафиксировать", "Отмена")) return;
        await RunCommandAsync(() => _process.ReportShortageAsync(_taskId,
            _warehouseId, cycle.Id, _task.SkuId, group.ExpirationDate,
            expected, found, CommentEntry.Text));
    }

    private async void OnReturnClicked(object? sender, EventArgs e) =>
        await CompleteSourceAsync(empty: false);

    private async void OnEmptyClicked(object? sender, EventArgs e) =>
        await CompleteSourceAsync(empty: true);

    private async Task CompleteSourceAsync(bool empty)
    {
        if (_busy || _process.HasPendingCommand || !CanOperate
            || ActiveCycle is not { } cycle || string.IsNullOrWhiteSpace(ReturnEntry.Text)) return;
        var code = ReturnEntry.Text.Trim();
        if (code != cycle.SourceCode)
        {
            ErrorLabel.Text = $"Повторно сканируйте исходную ячейку {cycle.SourceCode}.";
            return;
        }
        if (!await DisplayAlertAsync(empty ? "Ячейка пуста" : "Вернуть поддон",
            empty ? $"Подтвердить, что в {code} физически не осталось товара?"
                : $"Подтвердить возврат поддона в {code}?",
            "Подтвердить", "Отмена")) return;
        await RunCommandAsync(() => _process.CompleteSourceAsync(_taskId,
            _warehouseId, cycle.Id, code, empty));
    }

    private async void OnRetryClicked(object? sender, EventArgs e)
    {
        if (_busy || !_process.HasPendingCommand) return;
        await RunCommandAsync(_process.RetryAsync, retry: true);
    }

    private async Task RunCommandAsync(Func<Task<MobileReplenishmentTaskResponse>> action,
        bool retry = false)
    {
        if (_busy || _process.HasPendingCommand && !retry) return;
        SetBusy(true);
        ErrorLabel.Text = string.Empty;
        try
        {
            _task = await action();
            SourceEntry.Text = string.Empty;
            DestinationEntry.Text = string.Empty;
            QuantityEntry.Text = string.Empty;
            ExpectedEntry.Text = string.Empty;
            FoundEntry.Text = string.Empty;
            CommentEntry.Text = string.Empty;
            ReturnEntry.Text = string.Empty;
            Apply();
        }
        catch (MobileApiException exception) { ErrorLabel.Text = exception.Message; }
        catch (HttpRequestException)
        {
            ErrorLabel.Text = _process.HasPendingCommand
                ? "Ответ не подтверждён. Повторите ту же команду."
                : "Команда выполнена, но карточка не обновилась. Потяните вниз.";
        }
        finally { SetBusy(false); await UpdateCameraAsync(); }
    }

    private void SetBusy(bool busy)
    {
        Interlocked.Increment(ref _scanGeneration);
        _busy = busy;
        ProgressIndicator.IsVisible = busy;
        RetryButton.IsVisible = !busy && _process.HasPendingCommand;
        if (RetryButton.IsVisible) RetryButton.Text = $"Повторить: {_process.PendingLabel}";
        NavigationPage.SetHasBackButton(this, !busy && !_process.HasPendingCommand);
        if (busy) CameraScannerView.Stop();
        else if (_task is not null) Apply();
    }

    protected override bool OnBackButtonPressed()
    {
        if (!_busy && !_process.HasPendingCommand) return base.OnBackButtonPressed();
        ErrorLabel.Text = "Сначала завершите повтор команды.";
        return true;
    }

    private static bool TryQuantity(string? value, out decimal quantity) =>
        decimal.TryParse(value, NumberStyles.Number,
            CultureInfo.CurrentCulture, out quantity) && quantity > 0;

    private enum ScanTarget { Source, Destination, Return }
}

public sealed record ReplenishmentGroupRow(DateOnly? ExpirationDate, decimal Available)
{
    public string DisplayText => (ExpirationDate is DateOnly date
        ? $"до {date:dd.MM.yyyy}" : "без срока")
        + $" · доступно {Available.ToString("0.###", CultureInfo.CurrentCulture)}";
}

public sealed record ReplenishmentStockRow(string SkuName, bool IsTaskSku,
    DateOnly? ExpirationDate, decimal Accounting, decimal Blocked)
{
    public string Text => $"{(IsTaskSku ? "● " : "○ ")}{SkuName} · "
        + (ExpirationDate is DateOnly date ? $"до {date:dd.MM.yyyy}" : "без срока")
        + $" · учёт {Accounting:0.###}, спорное {Blocked:0.###}";
}

public sealed record ReplenishmentTransferRow(string SourceCode,
    string DestinationCode, DateOnly? ExpirationDate, decimal Quantity)
{
    public string Text => $"{SourceCode} → {DestinationCode} · {Quantity:0.###} · "
        + (ExpirationDate is DateOnly date ? $"до {date:dd.MM.yyyy}" : "без срока");
}

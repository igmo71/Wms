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
    private bool _visible;
    private bool _busy;
    private bool _confirmingAddress;
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
        if (_busy || _confirmingAddress || _taskId == Guid.Empty) return;
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
        StatusLabel.Text = _task.Status switch
        {
            MobileReplenishmentTaskStatus.Completed => "Задание завершено",
            MobileReplenishmentTaskStatus.Cancelled => "Задание отменено",
            _ when cycle is not null => "Поддон снят",
            _ when _task.Cycles.Count > 0 => "Нужен следующий поддон",
            _ => "Ожидается снятие поддона"
        };
        var canStart = cycle is null && Remaining > 0 && CanOperate
            && _task.Status is MobileReplenishmentTaskStatus.Ordered or MobileReplenishmentTaskStatus.InProgress;
        var reserveCode = cycle?.SourceCode ?? _task.RecommendedSourceCode;
        ReservePanel.IsVisible = canStart || cycle is not null;
        ReserveAddressButton.Text = reserveCode;
        ReserveAddressButton.IsVisible = !string.IsNullOrWhiteSpace(reserveCode);
        ReserveActionLabel.Text = cycle is not null ? "Возврат поддона в «Хранение»"
            : ReserveAddressButton.IsVisible ? "Снять поддон из «Хранения»" : "Сканируйте ячейку «Хранения»";
        ReturnInstructionLabel.IsVisible = cycle is not null && CanOperate;
        ActivePanel.IsVisible = cycle is not null;
        if (cycle is not null)
        {
            DestinationAddressButton.Text = _task.RecommendedDestinationCode;
            DestinationAddressButton.IsVisible = !string.IsNullOrWhiteSpace(_task.RecommendedDestinationCode);
            DestinationScanLabel.IsVisible = !DestinationAddressButton.IsVisible;
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
            StockRows = _task.ActiveSourceStock
                .Where(x => x.SkuId != _task.SkuId && x.AvailableQuantity > 0)
                .Select(x => new ReplenishmentStockRow(x.SkuName ?? x.SkuId.ToString(),
                    x.ExpirationDate, x.AvailableQuantity)).ToArray();
            OnPropertyChanged(nameof(StockRows));
            OtherStockPanel.IsVisible = StockRows.Count > 0;
            if (string.IsNullOrWhiteSpace(QuantityEntry.Text) && SelectedGroup is { } group)
                QuantityEntry.Text = Math.Min(Remaining, group.Available)
                    .ToString("0.###", CultureInfo.CurrentCulture);
            TransferPanel.IsVisible = CanOperate && Remaining > 0 && groups.Length > 0;
        }
        else
        {
            StockRows = [];
            OnPropertyChanged(nameof(StockRows));
            GroupPicker.ItemsSource = null;
        }
        TransferRows = _task.Cycles.SelectMany(x => x.Transfers.Select(t =>
            new ReplenishmentTransferRow(
                string.IsNullOrWhiteSpace(x.SourceName) ? x.SourceCode : x.SourceName,
                string.IsNullOrWhiteSpace(t.DestinationName) ? t.DestinationCode : t.DestinationName,
                t.ExpirationDate, t.EffectiveQuantity))).ToArray();
        OnPropertyChanged(nameof(TransferRows));
    }

    private void OnGroupChanged(object? sender, EventArgs e)
    {
        if (SelectedGroup is { } group)
            QuantityEntry.Text = Math.Min(Remaining, group.Available)
                .ToString("0.###", CultureInfo.CurrentCulture);
    }

    private async void OnReserveAddressClicked(object? sender, EventArgs e) =>
        await ConfirmAddressAsync(ActiveCycle?.SourceCode ?? _task?.RecommendedSourceCode);

    private async void OnDestinationAddressClicked(object? sender, EventArgs e) =>
        await ConfirmAddressAsync(_task?.RecommendedDestinationCode);

    private void OnScanReceived(object? sender, BarcodeScanEvent scan)
    {
        var generation = Volatile.Read(ref _scanGeneration);
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            if (!_visible || generation != Volatile.Read(ref _scanGeneration)) return;
            if (scan.Source == BarcodeScanSource.Camera)
            {
                _cameraRequested = false;
                await UpdateCameraAsync();
            }
            await ConfirmAddressAsync(scan.Value);
        });
    }

    private async void OnCameraClicked(object? sender, EventArgs e)
    {
        if (_busy || _confirmingAddress || _process.HasPendingCommand
            || _scanner.ActiveSource != BarcodeScanSource.Camera) return;
        _cameraRequested = !_cameraRequested;
        await UpdateCameraAsync();
    }

    private async Task UpdateCameraAsync()
    {
        CameraButton.Text = _cameraRequested ? "Закрыть камеру" : "Сканировать ячейку";
        if (_visible && !_busy && !_confirmingAddress && !_process.HasPendingCommand && _cameraRequested
            && _scanner.ActiveSource == BarcodeScanSource.Camera)
            await CameraScannerView.StartAsync();
        else CameraScannerView.Stop();
    }

    private Task ConfirmAddressAsync(string? address)
    {
        if (!_visible || _busy || _confirmingAddress || _process.HasPendingCommand
            || !CanOperate || _task is not { } task || string.IsNullOrWhiteSpace(address))
            return Task.CompletedTask;

        var code = address.Trim();
        var cycle = ActiveCycle;
        if (cycle is null)
        {
            if (!ReservePanel.IsVisible) return Task.CompletedTask;
            return ConfirmCommandAsync("Снять поддон", $"Снять поддон из ячейки {code}?",
                "Подтвердить", () => _process.StartSourceAsync(_taskId, _warehouseId, code));
        }

        if (string.Equals(code, cycle.SourceCode, StringComparison.OrdinalIgnoreCase))
            return ConfirmCommandAsync("Завершить работу с поддоном",
                $"Подтвердить завершение работы с ячейкой {cycle.SourceCode}?", "Завершить",
                () => _process.CompleteSourceAsync(_taskId, _warehouseId, cycle.Id, cycle.SourceCode));

        if (!TransferPanel.IsVisible || SelectedGroup is not { } group)
        {
            ErrorLabel.Text = $"Сканируйте исходную ячейку {cycle.SourceCode} или нажмите на её карточку.";
            return Task.CompletedTask;
        }
        if (!TryQuantity(QuantityEntry.Text, out var quantity)
            || quantity > Remaining || quantity > group.Available)
        {
            ErrorLabel.Text = "Укажите положительное количество с точностью до 0,001 в пределах остатка заказа и группы срока.";
            return Task.CompletedTask;
        }

        return ConfirmCommandAsync("Провести подпитку",
            $"Перенести {quantity:0.###} из ячейки {cycle.SourceCode} в {code}?", "Провести",
            () => _process.TransferAsync(_taskId, _warehouseId, cycle.Id,
                code, task.SkuId, group.ExpirationDate, quantity));
    }

    private async Task ConfirmCommandAsync(string title, string message, string accept,
        Func<Task<MobileReplenishmentTaskResponse>> action)
    {
        _confirmingAddress = true;
        Interlocked.Increment(ref _scanGeneration);
        ErrorLabel.Text = string.Empty;
        try
        {
            if (_cameraRequested)
            {
                _cameraRequested = false;
                await UpdateCameraAsync();
            }
            if (await DisplayAlertAsync(title, message, accept, "Отмена") && _visible)
                await RunCommandAsync(action);
        }
        finally
        {
            Interlocked.Increment(ref _scanGeneration);
            _confirmingAddress = false;
        }
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
            QuantityEntry.Text = string.Empty;
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
        var canSelectAddress = !busy && !_process.HasPendingCommand && CanOperate;
        ReserveAddressButton.IsEnabled = DestinationAddressButton.IsEnabled = canSelectAddress;
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
            CultureInfo.CurrentCulture, out quantity)
        && quantity > 0 && WarehouseQuantityInput.IsSupported(quantity);
}

public sealed record ReplenishmentGroupRow(DateOnly? ExpirationDate, decimal Available)
{
    public string DisplayText => (ExpirationDate is DateOnly date
        ? $"до {date:dd.MM.yyyy}" : "без срока")
        + $" · доступно {Available.ToString("0.###", CultureInfo.CurrentCulture)}";
}

public sealed record ReplenishmentStockRow(string SkuName,
    DateOnly? ExpirationDate, decimal AvailableQuantity)
{
    public string Text => $"{SkuName} · "
        + (ExpirationDate is DateOnly date ? $"до {date:dd.MM.yyyy}" : "без срока")
        + $" · {AvailableQuantity:0.###}";
}

public sealed record ReplenishmentTransferRow(string SourceCode,
    string DestinationCode, DateOnly? ExpirationDate, decimal Quantity)
{
    public string Text => $"{SourceCode} → {DestinationCode} · {Quantity:0.###} · "
        + (ExpirationDate is DateOnly date ? $"до {date:dd.MM.yyyy}" : "без срока");
}

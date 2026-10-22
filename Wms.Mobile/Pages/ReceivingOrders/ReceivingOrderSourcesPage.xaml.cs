using Microsoft.Extensions.DependencyInjection;
using Wms.Contracts.Mobile.V1;
using Wms.Mobile.Scanning;
using Wms.Mobile.Services;

namespace Wms.Mobile;

public partial class ReceivingOrderSourcesPage : ContentPage
{
    private readonly MobileReceivingOrderClient _orders;
    private readonly IOperationalBarcodeScanner _scanner;
    private readonly IServiceProvider _services;
    private ReceivingOrderReceivingProcess _process;
    private MobileReceivingWorkOrderResponse? _order;
    private Guid? _warehouseId;
    private Guid? _initialSourceRefKey;
    private bool _creating;
    private (Guid RequestId, Guid SourceRefKey)? _pendingCreate;
    private IReadOnlyList<MobileReceivingLinkedSourceResponse> _sources = [];
    private HashSet<Guid> _beforeChange = [];
    private Guid? _newSourceRefKey;
    private bool _visible;
    private bool _busy;
    private long _scanGeneration;

    public ReceivingOrderSourcesPage(MobileReceivingOrderClient orders,
        IOperationalBarcodeScanner scanner, ReceivingOrderReceivingProcess process,
        IServiceProvider services)
    {
        InitializeComponent();
        _orders = orders;
        _scanner = scanner;
        _process = process;
        _services = services;
        BindingContext = this;
        CameraScannerView.Configure(scanner);
    }

    public IReadOnlyList<ReceivingSourceCompactRow> SourceRows { get; private set; } = [];

    public void Show(MobileReceivingWorkOrderResponse order,
        ReceivingOrderReceivingProcess process)
    {
        _creating = false;
        _order = order;
        NumberLabel.Text = $"Ордер {order.Number}";
        _process = process;
        _newSourceRefKey = null;
    }

    public void ShowForCreation(Guid warehouseId, Guid? initialSourceRefKey = null)
    {
        _creating = true;
        _warehouseId = warehouseId;
        _initialSourceRefKey = initialSourceRefKey;
        _order = null;
        _newSourceRefKey = null;
        ShowEmptyState();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _visible = true;
        _scanner.ScanReceived += OnScanReceived;
        if (_initialSourceRefKey is { } sourceRefKey)
        {
            _initialSourceRefKey = null;
            await CreateInitialSourceAsync(sourceRefKey);
        }
        else await RefreshAsync();
        await UpdateCameraAsync();
    }

    private async Task CreateInitialSourceAsync(Guid sourceRefKey)
    {
        SetBusy(true);
        ErrorLabel.Text = string.Empty;
        try
        {
            _pendingCreate = (Guid.NewGuid(), sourceRefKey);
            await RetryCreateAsync();
        }
        catch (MobileApiException ex) { ErrorLabel.Text = ex.Message; }
        catch (HttpRequestException)
        {
            ErrorLabel.Text = HasPendingCommand
                ? "Ответ не подтверждён. Повторите создание ордера."
                : _order is not null
                    ? "Ордер создан, но список документов не обновился. Потяните вниз для обновления."
                    : "Сервер WMS недоступен. Повторите сканирование.";
        }
        finally { SetBusy(false); }
    }

    protected override void OnDisappearing()
    {
        _visible = false;
        Interlocked.Increment(ref _scanGeneration);
        _scanner.ScanReceived -= OnScanReceived;
        CameraScannerView.Stop();
        base.OnDisappearing();
    }

    private async void OnRefreshing(object? sender, EventArgs e)
    {
        await RefreshAsync();
        SourcesRefreshView.IsRefreshing = false;
    }

    private async Task RefreshAsync()
    {
        if (_busy) return;
        if (_order is null)
        {
            ShowEmptyState();
            await UpdateCameraAsync();
            return;
        }
        SetBusy(true);
        try
        {
            _order = await _orders.GetAsync(_order.Id);
            await RefreshSourcesAsync();
            ErrorLabel.Text = string.Empty;
        }
        catch (MobileApiException ex) { ErrorLabel.Text = ex.Message; }
        catch (HttpRequestException) { ErrorLabel.Text = "Не удалось обновить документы 1С."; }
        finally { SetBusy(false); await UpdateCameraAsync(); }
    }

    private async Task RefreshSourcesAsync()
    {
        if (_order is null) return;
        _sources = await _orders.GetLinkedSourcesAsync(_order.Id);
        var canDetach = CanAttach && _order.Lpns.All(x => x.Status is not
            (MobileLicensePlateNumberStatus.Closed or MobileLicensePlateNumberStatus.Deleted));
        SourceRows = _sources.Select(source => new ReceivingSourceCompactRow(
            source.RefKey,
            $"{(source.LastAttemptError is null ? "" : "⚠ ")}{source.Number ?? source.RefKey.ToString()} · {SourceDate(source.Date)}",
            $"{source.SenderName ?? source.Sender ?? "—"} · {source.WarehouseOperation ?? "—"}",
            canDetach,
            source.RefKey == _newSourceRefKey ? Colors.LightGray : Colors.White)).ToArray();
        OnPropertyChanged(nameof(SourceRows));
        EmptyLabel.IsVisible = SourceRows.Count == 0;
        EmptyLabel.Text = "В ордере нет документов 1С.";
        UpdateRecovery();
        ScanHintLabel.Text = CanAttach
            ? "Сканируйте документ 1С. Он сразу прикрепится к ордеру."
            : "Прикрепление документов в текущем статусе недоступно.";
    }

    private void ShowEmptyState()
    {
        SourceRows = [];
        OnPropertyChanged(nameof(SourceRows));
        EmptyLabel.IsVisible = true;
        EmptyLabel.Text = "Документы 1С ещё не добавлены.";
        ScanHintLabel.Text = "Сканируйте первый документ 1С, чтобы создать ордер.";
        UpdateRecovery();
    }

    private bool CanAttach => (_creating && _order is null)
        || _order?.Status is (MobileReceivingOrderStatus.ReadyForReceiving
            or MobileReceivingOrderStatus.InReceiving);

    private void OnScanReceived(object? sender, BarcodeScanEvent scan)
    {
        var generation = Volatile.Read(ref _scanGeneration);
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            if (_visible && generation == Volatile.Read(ref _scanGeneration))
                await HandleScanAsync(scan.Value);
        });
    }

    private async Task HandleScanAsync(string barcode)
    {
        if (!_visible || _busy || HasPendingCommand || !CanAttach)
            return;
        if (_order is null)
        {
            await CreateFromScanAsync(barcode);
            return;
        }
        _beforeChange = _order.SourceRefKeys.ToHashSet();
        await ExecuteAsync(async () =>
        {
            _order = await _process.AttachSourceAsync(_order.Id, barcode);
            MarkNewSource();
            await RefreshSourcesAsync();
        });
    }

    private async Task CreateFromScanAsync(string barcode)
    {
        if (_warehouseId is not { } warehouseId) return;
        SetBusy(true);
        ErrorLabel.Text = string.Empty;
        try
        {
            var resolved = await _orders.ResolveDocumentAsync(warehouseId, barcode);
            if (resolved.Order is { } linkedOrder)
            {
                if (await DisplayAlertAsync("Документ уже прикреплён",
                    $"Документ находится в ордере {linkedOrder.Number}. Открыть его?",
                    "Открыть", "Остаться"))
                    await OpenOrderAsync(linkedOrder);
                return;
            }
            if (resolved.Source is not { } source) return;
            _pendingCreate = (Guid.NewGuid(), source.RefKey);
            await RetryCreateAsync();
        }
        catch (MobileApiException ex) { ErrorLabel.Text = ex.Message; }
        catch (HttpRequestException)
        {
            ErrorLabel.Text = HasPendingCommand
                ? "Ответ не подтверждён. Повторите создание ордера."
                : _order is not null
                    ? "Ордер создан, но список документов не обновился. Потяните вниз для обновления."
                    : "Сервер WMS недоступен. Повторите сканирование.";
        }
        finally { SetBusy(false); await UpdateCameraAsync(); }
    }

    private async Task RetryCreateAsync()
    {
        if (_pendingCreate is not { } pending) return;
        try
        {
            var created = await _orders.CreateOrderAsync(
                pending.RequestId, [pending.SourceRefKey]);
            _order = await _orders.GetAsync(created.ResourceId);
            _pendingCreate = null;
            _newSourceRefKey = pending.SourceRefKey;
            await RefreshSourcesAsync();
        }
        catch (MobileApiException)
        {
            _pendingCreate = null;
            throw;
        }
    }

    private async Task OpenOrderAsync(MobileReceivingWorkOrderResponse order)
    {
        var page = _services.GetRequiredService<ReceivingOrderSkuPage>();
        page.Show(order, _process);
        await Navigation.PushAsync(page);
        Navigation.RemovePage(this);
    }

    private void MarkNewSource()
    {
        _newSourceRefKey = _order?.SourceRefKeys.FirstOrDefault(x => !_beforeChange.Contains(x));
        if (_newSourceRefKey == Guid.Empty) _newSourceRefKey = null;
    }

    private async void OnDetachClicked(object? sender, EventArgs e)
    {
        if (_busy || HasPendingCommand || _order is null
            || sender is not Button { CommandParameter: Guid refKey }
            || !SourceRows.Any(x => x.RefKey == refKey && x.CanDetach)) return;
        if (!await DisplayAlertAsync("Открепить документ 1С",
            "Документ будет исключён из текущего плана. Продолжить?",
            "Открепить", "Отмена")) return;
        await ExecuteAsync(async () =>
        {
            _order = await _process.DetachSourceAsync(_order.Id, refKey);
            if (_newSourceRefKey == refKey) _newSourceRefKey = null;
            await RefreshSourcesAsync();
        });
    }

    private async void OnSourceTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is not Guid refKey || _busy) return;
        var source = _sources.FirstOrDefault(x => x.RefKey == refKey);
        if (source is null) return;
        await DisplayAlertAsync("Документ 1С", string.Join("\n", new[]
        {
            $"{source.Number ?? "—"} · {SourceDate(source.Date)}",
            source.SenderName ?? source.Sender ?? "—",
            source.WarehouseOperation ?? "—",
            $"Хозяйственная операция: {source.BusinessOperation ?? "—"}",
            $"Основание: {source.BasisDocument ?? "—"}",
            $"Ref_Key: {source.RefKey}",
            source.LastAttemptError is null ? string.Empty
                : $"Ошибка сверки: {source.LastAttemptError}"
        }), "Закрыть");
    }

    private async void OnRetryClicked(object? sender, EventArgs e)
    {
        if (_busy || !HasPendingCommand) return;
        if (_pendingCreate is not null)
        {
            SetBusy(true);
            ErrorLabel.Text = string.Empty;
            try { await RetryCreateAsync(); }
            catch (MobileApiException ex) { ErrorLabel.Text = ex.Message; }
            catch (HttpRequestException)
            {
                ErrorLabel.Text = HasPendingCommand
                    ? "Ответ не подтверждён. Повторите создание ордера."
                    : "Ордер создан, но список документов не обновился. Потяните вниз для обновления.";
            }
            finally { SetBusy(false); await UpdateCameraAsync(); }
            return;
        }
        await ExecuteAsync(async () =>
        {
            var attaching = _process.PendingLabel == "Добавить источник";
            _order = await _process.RetryAsync();
            if (attaching) MarkNewSource();
            await RefreshSourcesAsync();
        }, retry: true);
    }

    private async void OnJoinClicked(object? sender, EventArgs e)
    {
        if (_order is null || _busy || HasPendingCommand) return;
        var page = _services.GetRequiredService<ReceivingOrderJoinPage>();
        page.Show(_order, _process);
        await Navigation.PushAsync(page);
    }

    private async void OnSkuClicked(object? sender, EventArgs e)
    {
        if (_order is null || _busy || HasPendingCommand) return;
        var page = _services.GetRequiredService<ReceivingOrderSkuPage>();
        page.Show(_order, _process);
        await Navigation.PushAsync(page);
        Navigation.RemovePage(this);
    }

    private async void OnLpnsClicked(object? sender, EventArgs e)
    {
        if (_order is null || _busy || HasPendingCommand) return;
        var page = _services.GetRequiredService<ReceivingOrderLpnListPage>();
        page.Show(_order, _process);
        await Navigation.PushAsync(page);
        Navigation.RemovePage(this);
    }

    private async Task ExecuteAsync(Func<Task> action, bool retry = false)
    {
        if (_busy || HasPendingCommand && !retry) return;
        SetBusy(true);
        ErrorLabel.Text = string.Empty;
        try { await action(); }
        catch (MobileApiException ex) { ErrorLabel.Text = ex.Message; }
        catch (HttpRequestException)
        {
            ErrorLabel.Text = HasPendingCommand
                ? "Ответ не подтверждён. Повторите ту же команду."
                : "Изменение выполнено, но список документов не обновился. Потяните вниз для обновления.";
        }
        catch (InvalidOperationException ex) { ErrorLabel.Text = ex.Message; }
        finally { SetBusy(false); await UpdateCameraAsync(); }
    }

    private void UpdateRecovery()
    {
        NumberLabel.Text = _order is null ? "Новый ордер" : $"Ордер {_order.Number}";
        JoinButton.Text = _order?.Status == MobileReceivingOrderStatus.ReadyForReceiving
            ? "Начать" : "Присоединиться";
        RetryButton.IsVisible = HasPendingCommand;
        if (RetryButton.IsVisible) RetryButton.Text = _pendingCreate is not null
            ? "Повторить создание ордера"
            : $"Повторить: {_process.PendingLabel}";
        SectionsGrid.IsVisible = _order is not null;
        SkuButton.IsEnabled = !_busy && !HasPendingCommand;
        LpnsButton.IsEnabled = !_busy && !HasPendingCommand;
        JoinButton.IsVisible = _order is { IsParticipant: false,
            Status: MobileReceivingOrderStatus.ReadyForReceiving or MobileReceivingOrderStatus.InReceiving };
        JoinButton.IsEnabled = !_busy && !HasPendingCommand;
        NavigationPage.SetHasBackButton(this, !_busy && !HasPendingCommand);
    }

    private bool HasPendingCommand => _pendingCreate is not null || _process.HasPendingCommand;

    protected override bool OnBackButtonPressed()
    {
        if (!_busy && !HasPendingCommand) return base.OnBackButtonPressed();
        ErrorLabel.Text = "Сначала завершите повтор команды.";
        return true;
    }

    private void SetBusy(bool busy)
    {
        Interlocked.Increment(ref _scanGeneration);
        _busy = busy;
        ProgressIndicator.IsVisible = busy;
        UpdateRecovery();
    }

    private async Task UpdateCameraAsync()
    {
        if (_visible && !_busy && !HasPendingCommand && CanAttach
            && _scanner.ActiveSource == BarcodeScanSource.Camera)
            await CameraScannerView.StartAsync();
        else CameraScannerView.Stop();
    }

    private static string SourceDate(string? value) =>
        DateTime.TryParse(value, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var date)
            ? date.ToString("dd.MM.yyyy") : value ?? "—";
}

public sealed record ReceivingSourceCompactRow(Guid RefKey, string FirstLine,
    string SecondLine, bool CanDetach, Color BackgroundColor);

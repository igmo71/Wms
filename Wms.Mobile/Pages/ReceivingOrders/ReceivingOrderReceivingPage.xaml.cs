using Microsoft.Extensions.DependencyInjection;
using Wms.Contracts.Mobile.V1;
using Wms.Mobile.Scanning;
using Wms.Mobile.Services;

namespace Wms.Mobile;

public partial class ReceivingOrderReceivingPage : ContentPage
{
    private readonly ReceivingOrderReceivingProcess _process;
    private readonly MobileReceivingOrderClient _orders;
    private readonly MobileReferenceDataClient _reference;
    private readonly IOperationalBarcodeScanner _scanner;
    private readonly IServiceProvider _services;
    private MobileReceivingWorkOrderResponse? _order;
    private IReadOnlyList<MobileReceivingLinkedSourceResponse> _linkedSources = [];
    private bool _visible;
    private bool _busy;
    private bool _lpnTab;
    private bool _sourceScanMode;
    private long _scanGeneration;

    public ReceivingOrderReceivingPage(ReceivingOrderReceivingProcess process,
        MobileReceivingOrderClient orders, MobileReferenceDataClient reference,
        IOperationalBarcodeScanner scanner, IServiceProvider services)
    {
        InitializeComponent();
        _process = process;
        _orders = orders;
        _reference = reference;
        _scanner = scanner;
        _services = services;
        BindingContext = this;
        CameraScannerView.Configure(scanner);
    }

    public IReadOnlyList<ReceivingWorkItemRow> ItemRows { get; private set; } = [];
    public IReadOnlyList<ReceivingWorkLpnRow> LpnRows { get; private set; } = [];
    public IReadOnlyList<ReceivingWorkLocationRow> LocationRows { get; private set; } = [];
    public IReadOnlyList<ReceivingLinkedSourceRow> SourceRows { get; private set; } = [];

    public void Show(MobileReceivingWorkOrderResponse order)
    {
        _linkedSources = [];
        _sourceScanMode = false;
        Apply(order);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _visible = true;
        _scanner.ScanReceived += OnScanReceived;
        await RefreshAsync();
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

    private async void OnRefreshing(object? sender, EventArgs e)
    {
        await RefreshAsync();
        OrderRefreshView.IsRefreshing = false;
    }

    private async Task RefreshAsync()
    {
        if (_busy || _order is null) return;
        SetBusy(true);
        try
        {
            Apply(await _orders.GetAsync(_order.Id));
            await RefreshLinkedSourcesAsync();
        }
        catch (MobileApiException ex) { ErrorLabel.Text = ex.Message; }
        catch (HttpRequestException) { ErrorLabel.Text = "Не удалось обновить ордер."; }
        finally { SetBusy(false); }
    }

    private void Apply(MobileReceivingWorkOrderResponse order)
    {
        _order = order;
        NumberLabel.Text = $"Ордер WMS {order.Number}";
        CreatedAtLabel.Text = $"Создан: {order.CreatedAtUtc.ToLocalTime():dd.MM.yyyy HH:mm:ss}";
        StatusLabel.Text = order.Status switch
        {
            MobileReceivingOrderStatus.ReadyForReceiving => "К приёмке",
            MobileReceivingOrderStatus.InReceiving => "В работе",
            MobileReceivingOrderStatus.Received => "Принят",
            _ => order.Status.ToString()
        };
        LocationLabel.Text = order.ReceivingLocationId is Guid id
            ? $"Склад: {order.WarehouseName ?? order.WarehouseId.ToString()} · Место приёмки: {order.ReceivingLocationAddress ?? id.ToString()}"
            : $"Склад: {order.WarehouseName ?? order.WarehouseId.ToString()} · Место приёмки ещё не выбрано";
        JoinButton.IsVisible = !order.IsParticipant
            && (order.Status is MobileReceivingOrderStatus.ReadyForReceiving
                or MobileReceivingOrderStatus.InReceiving);
        LocationPanel.IsVisible = !order.IsParticipant && LocationPanel.IsVisible
            && order.Status == MobileReceivingOrderStatus.ReadyForReceiving;
        SourceScanButton.IsVisible = order.Status is MobileReceivingOrderStatus.ReadyForReceiving
            or MobileReceivingOrderStatus.InReceiving;
        if (!SourceScanButton.IsVisible) _sourceScanMode = false;
        SourceScanButton.Text = _sourceScanMode ? "Отменить добавление источника"
            : "Добавить источник сканированием";
        UpdateScanHint();
        ItemRows = order.Items.Where(item => item.IsCurrent).Select((item, index) =>
        {
            var exact = item.PlanQuantity is decimal plan
                && item.FactQuantity is decimal fact && plan == fact;
            var activeLpn = order.Lpns.Any(x =>
                (x.Status is MobileLicensePlateNumberStatus.Open or MobileLicensePlateNumberStatus.Closed)
                && x.Items.Any(y => y.SkuId == item.SkuId));
            return new ReceivingWorkItemRow(item.SkuId,
                $"{index + 1}. {item.SkuName ?? item.SkuId.ToString()}",
                $"План: {Format(item.PlanQuantity)} · Факт: {Format(item.FactQuantity)} · {item.UnitOfMeasure ?? "—"}",
                order.IsParticipant && !activeLpn && item.FactQuantity is null,
                exact ? Colors.LightGreen : item.FactQuantity is null
                    ? Colors.White : Colors.LightYellow);
        }).ToArray();
        LpnRows = order.Lpns.Where(x => x.Status is not
            (MobileLicensePlateNumberStatus.Cancelled or MobileLicensePlateNumberStatus.Deleted))
            .Select(x => new ReceivingWorkLpnRow(x.Id, x.Code,
                $"{(x.Status == MobileLicensePlateNumberStatus.Closed ? "Закрыта" : "Открыта")} · "
                + string.Join("; ", x.Items.Select(y =>
                    $"{y.SkuName ?? y.SkuId.ToString()}: {y.Quantity:0.###} {y.UnitOfMeasure ?? "—"}")),
                x.Status == MobileLicensePlateNumberStatus.Closed
                    && x.Items.Any(y => y.RemainingQuantity > 0))).ToArray();
        OnPropertyChanged(nameof(ItemRows));
        OnPropertyChanged(nameof(LpnRows));
        UpdateSourceRows();
        SetTab(_lpnTab);
        UpdateRecovery();
    }

    private void UpdateScanHint()
    {
        ScanHintLabel.Text = _sourceScanMode
            ? "Отсканируйте штрихкод документа 1С для добавления в этот ордер."
            : _order?.IsParticipant == true
                ? "Сканируйте выданную этикетку LPN. SKU сканируется внутри LPN."
                : "Присоединитесь к циклу приёмки, чтобы открыть LPN.";
    }

    private async Task RefreshLinkedSourcesAsync()
    {
        if (_order is null) return;
        try
        {
            _linkedSources = await _orders.GetLinkedSourcesAsync(_order.Id);
            UpdateSourceRows();
        }
        catch (MobileApiException ex) { ErrorLabel.Text = ex.Message; }
        catch (HttpRequestException) { ErrorLabel.Text = "Не удалось обновить список источников."; }
    }

    private void UpdateSourceRows()
    {
        if (_order is null) return;
        var canDetach = (_order.Status is MobileReceivingOrderStatus.ReadyForReceiving
            or MobileReceivingOrderStatus.InReceiving)
            && _order.Lpns.All(x => x.Status is not
                (MobileLicensePlateNumberStatus.Closed or MobileLicensePlateNumberStatus.Deleted));
        SourceRows = _order.SourceRefKeys.Select(refKey =>
        {
            var source = _linkedSources.FirstOrDefault(x => x.RefKey == refKey);
            return new ReceivingLinkedSourceRow(refKey,
                source?.Number ?? refKey.ToString(),
                source is null ? string.Empty : string.Join("\n", new[]
                {
                    $"Дата: {SourceDate(source.Date)}",
                    $"Отправитель: {source.SenderName ?? source.Sender ?? "—"}"
                        + (source.SenderType == "StandardODATA.Catalog_Партнеры" ? " (партнёр 1С)" : ""),
                    $"Складская операция: {source.WarehouseOperation ?? "—"}",
                    $"Хозяйственная операция: {source.BusinessOperation ?? "—"}",
                    $"Основание: {BasisDisplay(source)}",
                }), canDetach);
        }).ToArray();
        OnPropertyChanged(nameof(SourceRows));
        SourcesPanel.IsVisible = SourceRows.Count > 0;
    }

    private void OnSourceScanClicked(object? sender, EventArgs e)
    {
        if (_busy || _process.HasPendingCommand || _order?.Status is not
            (MobileReceivingOrderStatus.ReadyForReceiving or MobileReceivingOrderStatus.InReceiving))
            return;
        _sourceScanMode = !_sourceScanMode;
        SourceScanButton.Text = _sourceScanMode ? "Отменить добавление источника"
            : "Добавить источник сканированием";
        UpdateScanHint();
    }

    private void EndSourceScan()
    {
        _sourceScanMode = false;
        SourceScanButton.Text = "Добавить источник сканированием";
        UpdateScanHint();
    }

    private async void OnDetachSourceClicked(object? sender, EventArgs e)
    {
        if (_busy || _process.HasPendingCommand || _order is null
            || sender is not Button { CommandParameter: Guid refKey }
            || !SourceRows.Any(x => x.RefKey == refKey && x.CanDetach))
            return;
        if (!await DisplayAlertAsync("Открепить источник",
            "Источник будет исключён из текущего плана. Продолжить?", "Открепить", "Отмена"))
            return;
        await ExecuteAsync(async () =>
        {
            Apply(await _process.DetachSourceAsync(_order.Id, refKey));
            EndSourceScan();
            await RefreshLinkedSourcesAsync();
        });
    }

    private void OnScanReceived(object? sender, BarcodeScanEvent scan)
    {
        var generation = Volatile.Read(ref _scanGeneration);
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            if (_visible && generation == Volatile.Read(ref _scanGeneration))
                await HandleScanAsync(scan.Value);
        });
    }

    private async Task HandleScanAsync(string code)
    {
        if (!_visible || _busy || _process.HasPendingCommand || _order is null) return;
        if (_sourceScanMode)
        {
            await ExecuteAsync(async () =>
            {
                Apply(await _process.AttachSourceAsync(_order.Id, code));
                EndSourceScan();
                await RefreshLinkedSourcesAsync();
            });
            return;
        }
        if (LocationPanel.IsVisible)
        {
            await ResolveLocationAsync(code);
            return;
        }
        if (!_order.IsParticipant)
        {
            ErrorLabel.Text = "Сначала присоединитесь к приёмке.";
            return;
        }
        if (!code.Trim().StartsWith("LPN", StringComparison.OrdinalIgnoreCase))
        {
            ErrorLabel.Text = "SKU сканируется на экране LPN.";
            return;
        }
        await ExecuteAsync(async () =>
        {
            Apply(await _process.OpenLpnAsync(_order.Id, code.Trim()));
        });
        if (!_process.HasPendingCommand && _process.LastResourceId is Guid id)
            await OpenLpnPageAsync(id);
    }

    private async void OnJoinClicked(object? sender, EventArgs e)
    {
        if (_order is null || _busy || _process.HasPendingCommand) return;
        if (_order.Status == MobileReceivingOrderStatus.InReceiving)
        {
            await ExecuteAsync(async () => Apply(await _process.JoinAsync(_order.Id, null)));
            return;
        }
        LocationPanel.IsVisible = true;
        await SearchLocationsAsync();
    }

    private async void OnLocationSearch(object? sender, EventArgs e) => await SearchLocationsAsync();

    private async Task SearchLocationsAsync()
    {
        if (_order is null || _busy) return;
        SetBusy(true);
        try
        {
            var result = await _reference.FindReceivingLocationsAsync(_order.WarehouseId, LocationSearch.Text);
            LocationRows = result.Locations.Select(x => new ReceivingWorkLocationRow(
                x.Id, $"{x.Address} · {x.Name}")).ToArray();
            OnPropertyChanged(nameof(LocationRows));
        }
        catch (MobileApiException ex) { ErrorLabel.Text = ex.Message; }
        catch (HttpRequestException) { ErrorLabel.Text = "Не удалось найти места приёмки."; }
        finally { SetBusy(false); }
    }

    private async void OnLocationTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is Guid id) await JoinAtAsync(id);
    }

    private async Task ResolveLocationAsync(string barcode)
    {
        if (_order is null) return;
        SetBusy(true);
        try
        {
            var location = await _reference.ResolveStorageLocationAsync(
                barcode, _order.WarehouseId, MobileStorageLocationContext.Receiving);
            SetBusy(false);
            if (await DisplayAlertAsync("Место приёмки", $"Начать в {location.Address}?", "Да", "Нет"))
                await JoinAtAsync(location.Id);
        }
        catch (MobileApiException ex) { ErrorLabel.Text = ex.Message; }
        catch (HttpRequestException) { ErrorLabel.Text = "Не удалось проверить место приёмки."; }
        finally { SetBusy(false); await UpdateCameraAsync(); }
    }

    private async Task JoinAtAsync(Guid locationId)
    {
        if (_order is null || _busy || _process.HasPendingCommand) return;
        await ExecuteAsync(async () =>
        {
            Apply(await _process.JoinAsync(_order.Id, locationId));
            LocationPanel.IsVisible = false;
        });
    }

    private async void OnZeroClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: Guid skuId } || _order is null) return;
        if (!await DisplayAlertAsync("Проверено: 0", "Подтвердить отсутствие этого SKU?", "Да", "Нет")) return;
        await ExecuteAsync(async () => Apply(await _process.ConfirmZeroAsync(_order.Id, skuId)));
    }

    private async void OnLpnClicked(object? sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: Guid id }) await OpenLpnPageAsync(id);
    }

    private async Task OpenLpnPageAsync(Guid id)
    {
        if (_order is null || _busy || _process.HasPendingCommand) return;
        var page = _services.GetRequiredService<ReceivingOrderLicensePlateNumberPage>();
        page.Show(_order.Id, id, _process);
        await Navigation.PushAsync(page);
    }

    private async void OnPutawayClicked(object? sender, EventArgs e)
    {
        if (_order is null || _busy || _process.HasPendingCommand
            || sender is not Button { CommandParameter: Guid id }) return;
        var lpn = _order.Lpns.SingleOrDefault(x => x.Id == id);
        if (lpn is null) return;
        var page = _services.GetRequiredService<LicensePlateNumberPutawayDetailsPage>();
        page.Show(lpn);
        await Navigation.PushAsync(page);
    }

    private async void OnSkuClicked(object? sender, EventArgs e)
    {
        if (_order is null || _busy || _process.HasPendingCommand
            || sender is not Button { CommandParameter: Guid skuId }) return;
        var page = _services.GetRequiredService<ReceivingOrderSkuDistributionPage>();
        page.Show(_order.Id, skuId, _process);
        await Navigation.PushAsync(page);
    }

    private void OnItemsTabClicked(object? sender, EventArgs e) => SetTab(false);
    private void OnLpnsTabClicked(object? sender, EventArgs e) => SetTab(true);
    private void SetTab(bool lpns)
    {
        _lpnTab = lpns;
        ProductsPanel.IsVisible = !lpns;
        LpnsPanel.IsVisible = lpns;
    }

    private async Task ExecuteAsync(Func<Task> action)
    {
        if (_busy || _process.HasPendingCommand) return;
        SetBusy(true);
        ErrorLabel.Text = string.Empty;
        try { await action(); }
        catch (MobileApiException ex) { ErrorLabel.Text = ex.Message; }
        catch (HttpRequestException) { ErrorLabel.Text = "Ответ не подтверждён. Повторите ту же команду."; }
        catch (InvalidOperationException ex) { ErrorLabel.Text = ex.Message; }
        finally { SetBusy(false); UpdateRecovery(); await UpdateCameraAsync(); }
    }

    private async void OnRetryClicked(object? sender, EventArgs e)
    {
        if (_busy || !_process.HasPendingCommand) return;
        SetBusy(true);
        Guid? openedId = null;
        var succeeded = false;
        try
        {
            var opening = _process.PendingLabel == "Открыть LPN";
            var changingSource = _process.PendingLabel is "Добавить источник" or "Открепить источник";
            Apply(await _process.RetryAsync());
            if (opening) openedId = _process.LastResourceId;
            ErrorLabel.Text = string.Empty;
            if (changingSource)
            {
                EndSourceScan();
                await RefreshLinkedSourcesAsync();
            }
            succeeded = true;
        }
        catch (MobileApiException ex) { ErrorLabel.Text = ex.Message; }
        catch (HttpRequestException) { ErrorLabel.Text = "Ответ не подтверждён. Повторите ту же команду."; }
        finally { SetBusy(false); UpdateRecovery(); await UpdateCameraAsync(); }
        if (succeeded && openedId is Guid id) await OpenLpnPageAsync(id);
    }

    private void UpdateRecovery()
    {
        RetryButton.IsVisible = _process.HasPendingCommand;
        RetryButton.Text = $"Повторить: {_process.PendingLabel}";
        JoinButton.IsEnabled = !_busy && !_process.HasPendingCommand;
        SourceScanButton.IsEnabled = !_busy && !_process.HasPendingCommand;
        NavigationPage.SetHasBackButton(this, !_busy && !_process.HasPendingCommand);
    }

    protected override bool OnBackButtonPressed()
    {
        if (!_busy && !_process.HasPendingCommand) return base.OnBackButtonPressed();
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
        if (_visible && !_busy && !_process.HasPendingCommand
            && _scanner.ActiveSource == BarcodeScanSource.Camera)
            await CameraScannerView.StartAsync();
        else CameraScannerView.Stop();
    }

    private static string SourceDate(string? value) =>
        DateTime.TryParse(value, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var date)
            ? date.ToString("dd.MM.yyyy") : value ?? "—";

    private static string BasisDisplay(MobileReceivingLinkedSourceResponse source)
    {
        var type = source.BasisDocumentType switch
        {
            "StandardODATA.Document_ПриобретениеТоваровУслуг" => "приобретение товаров и услуг 1С",
            null => null,
            _ => "документ 1С"
        };
        return (source.BasisDocument ?? "—") + (type is null ? "" : $" ({type})");
    }

    private static string Format(decimal? value) => value?.ToString("0.###") ?? "?";
}

public sealed record ReceivingWorkItemRow(Guid SkuId, string Name,
    string PlanAndFact, bool CanConfirmZero, Color BackgroundColor);
public sealed record ReceivingWorkLpnRow(Guid Id, string Code,
    string Description, bool CanPutaway);
public sealed record ReceivingWorkLocationRow(Guid Id, string Display);
public sealed record ReceivingLinkedSourceRow(
    Guid RefKey, string Number, string Details, bool CanDetach);

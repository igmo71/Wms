using Microsoft.Extensions.DependencyInjection;
using Wms.Contracts.Mobile.V1;
using Wms.Mobile.Scanning;
using Wms.Mobile.Services;

namespace Wms.Mobile;

public partial class ReceivingOrderLpnListPage : ContentPage
{
    private ReceivingOrderReceivingProcess _process;
    private readonly MobileReceivingOrderClient _orders;
    private readonly IOperationalBarcodeScanner _scanner;
    private readonly IServiceProvider _services;
    private MobileReceivingWorkOrderResponse? _order;
    private Guid? _pendingSkuId;
    private bool _visible;
    private bool _busy;
    private long _scanGeneration;

    public ReceivingOrderLpnListPage(ReceivingOrderReceivingProcess process,
        MobileReceivingOrderClient orders, IOperationalBarcodeScanner scanner,
        IServiceProvider services)
    {
        InitializeComponent();
        _process = process;
        _orders = orders;
        _scanner = scanner;
        _services = services;
        BindingContext = this;
        CameraScannerView.Configure(scanner);
    }

    public IReadOnlyList<ReceivingWorkLpnRow> LpnRows { get; private set; } = [];

    public void Show(MobileReceivingWorkOrderResponse order,
        ReceivingOrderReceivingProcess process)
    {
        _process = process;
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
        try { Apply(await _orders.GetAsync(_order.Id)); }
        catch (MobileApiException ex) { ErrorLabel.Text = ex.Message; }
        catch (HttpRequestException) { ErrorLabel.Text = "Не удалось обновить ордер."; }
        finally { SetBusy(false); }
    }

    private void Apply(MobileReceivingWorkOrderResponse order)
    {
        _order = order;
        NumberLabel.Text = order.Number;
        var status = ReceivingStatusSymbols.ForOrder(order.Status);
        StatusIcon.Text = status.Glyph;
        StatusIcon.TextColor = status.TextColor;
        StatusIcon.FontAttributes = status.GlyphFontAttributes;
        SemanticProperties.SetDescription(StatusIcon, $"Статус ордера: {status.Description}");
        LocationLabel.Text = order.ReceivingLocationId is Guid id
            ? $"Место приёмки: {order.ReceivingLocationAddress ?? id.ToString()}"
            : "Место не выбрано";
        JoinButton.Text = order.Status == MobileReceivingOrderStatus.ReadyForReceiving
            ? "Начать" : "Присоединиться";
        JoinButton.IsVisible = !order.IsParticipant
            && order.Status is (MobileReceivingOrderStatus.ReadyForReceiving
                or MobileReceivingOrderStatus.InReceiving);
        CompleteButton.IsVisible = order.MayCompleteOrder;
        CompleteHintLabel.IsVisible = order.MayCompleteOrder && HasOpenLpn(order);
        LpnRows = order.Lpns.Where(x => x.Status is not
            (MobileLicensePlateNumberStatus.Cancelled or MobileLicensePlateNumberStatus.Deleted))
            .OrderBy(x => x.Items.Count == 0)
            .ThenBy(x => x.Items.SingleOrDefault() is { } item
                ? item.SkuName ?? item.SkuCode ?? item.SkuId.ToString() : string.Empty,
                StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(x => x.Code, StringComparer.OrdinalIgnoreCase)
            .Select(x =>
            {
                var lpnStatus = ReceivingStatusSymbols.ForLpn(x.Status);
                var item = x.Items.SingleOrDefault();
                var orderItem = item is null ? null
                    : order.Items.FirstOrDefault(y => y.SkuId == item.SkuId);
                var plan = orderItem?.PlanQuantity;
                var fact = orderItem?.FactQuantity;
                var unit = item?.UnitOfMeasure ?? "—";
                return new ReceivingWorkLpnRow(x.Id, x.Code,
                    lpnStatus.Glyph, $"Статус LPN: {lpnStatus.Description}", lpnStatus.TextColor,
                    item is null ? "SKU не выбран"
                        : item.SkuName ?? item.SkuCode ?? item.SkuId.ToString(),
                    item is null ? string.Empty : $"План: {Format(plan)} {unit}",
                    item is null ? string.Empty
                        : $"Факт: {ReceivingStatusSymbols.FactText(plan, fact)}",
                    ReceivingStatusSymbols.FactColor(plan, fact),
                    item is null ? string.Empty : $"В LPN: {item.Quantity:0.###}",
                    item is null ? string.Empty
                        : $"Срок: {item.ExpirationDate?.ToString("dd.MM.yyyy") ?? "не указан"}",
                    item is not null && (orderItem?.TracksExpirationDate == true
                        || item.ExpirationDate is not null),
                    item is not null,
                    x.Status == MobileLicensePlateNumberStatus.Open && order.IsParticipant,
                    x.Status == MobileLicensePlateNumberStatus.Closed,
                    item is not null && fact is null,
                    plan is decimal expected && fact == expected,
                    plan is decimal planned && fact is decimal actual && planned != actual,
                    item is null ? string.Empty
                        : ReceivingStatusSymbols.FactDescription(plan, fact));
            }).ToArray();
        OnPropertyChanged(nameof(LpnRows));
        UpdateRecovery();
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
        var scannedCode = code.Trim();
        if (!scannedCode.StartsWith("LPN", StringComparison.OrdinalIgnoreCase))
        {
            await OpenScannedSkuAsync(scannedCode);
            return;
        }
        var closedLpn = _order.Lpns.FirstOrDefault(x =>
            x.Status == MobileLicensePlateNumberStatus.Closed
            && string.Equals(x.Code, scannedCode, StringComparison.OrdinalIgnoreCase));
        if (closedLpn is not null)
        {
            await OpenLpnPageAsync(closedLpn.Id);
            return;
        }
        if (!_order.IsParticipant)
        {
            ErrorLabel.Text = "Сначала присоединитесь к приёмке.";
            return;
        }
        await ExecuteAsync(async () => Apply(await _process.OpenLpnAsync(_order.Id, scannedCode)));
        if (!_process.HasPendingCommand && _process.LastResourceId is Guid id)
            await OpenLpnPageAsync(id);
    }

    private async Task OpenScannedSkuAsync(string barcode)
    {
        if (_order is null) return;
        SetBusy(true);
        ErrorLabel.Text = string.Empty;
        Guid? skuToOpen = null;
        try
        {
            var sku = await _orders.ResolveSkuAsync(_order.Id, barcode);
            if (!_visible) return;
            if (!sku.IsInOrder)
            {
                if (_order is not { IsParticipant: true,
                        Status: MobileReceivingOrderStatus.InReceiving })
                {
                    ErrorLabel.Text = "Чтобы добавить SKU, присоединитесь к действующей приёмке.";
                    return;
                }
                var skuName = !string.IsNullOrWhiteSpace(sku.Name) ? sku.Name
                    : !string.IsNullOrWhiteSpace(sku.Code) ? sku.Code : sku.SkuId.ToString();
                if (!await DisplayAlertAsync("Нет в ордере", skuName,
                        "Добавить с планом 0", "Назад")) return;
                if (!_visible || _process.HasPendingCommand) return;
                _pendingSkuId = sku.SkuId;
                Apply(await _process.AddSkuAsync(_order.Id, sku.SkuId));
                _pendingSkuId = null;
            }
            skuToOpen = sku.SkuId;
        }
        catch (MobileApiException ex)
        {
            _pendingSkuId = null;
            ErrorLabel.Text = ex.Message;
        }
        catch (HttpRequestException)
        {
            if (!_process.HasPendingCommand) _pendingSkuId = null;
            ErrorLabel.Text = _process.HasPendingCommand
                ? "Ответ не подтверждён. Повторите ту же команду."
                : "Не удалось определить SKU. Повторите сканирование.";
        }
        catch (InvalidOperationException ex)
        {
            _pendingSkuId = null;
            ErrorLabel.Text = ex.Message;
        }
        finally { SetBusy(false); await UpdateCameraAsync(); }
        if (skuToOpen is Guid skuId && _visible) await OpenSkuDetailsAsync(skuId);
    }

    private async void OnJoinClicked(object? sender, EventArgs e)
    {
        if (_order is null || _busy || _process.HasPendingCommand) return;
        var page = _services.GetRequiredService<ReceivingOrderJoinPage>();
        page.Show(_order, _process);
        await Navigation.PushAsync(page);
    }

    private async void OnCompleteClicked(object? sender, EventArgs e)
    {
        if (_order is not { MayCompleteOrder: true } order || HasOpenLpn(order)
            || _busy || _process.HasPendingCommand) return;
        if (!await DisplayAlertAsync("Завершить ордер",
            $"Завершить приёмку ордера {order.Number}? Текущий план и факт будут сохранены.",
            "Завершить", "Отмена")) return;
        if (_busy || _process.HasPendingCommand
            || _order is not { MayCompleteOrder: true } current || HasOpenLpn(current)) return;
        var completed = false;
        await ExecuteAsync(async () =>
        {
            Apply(await _process.CompleteOrderAsync(current.Id));
            completed = true;
        });
        if (completed) await Navigation.PopAsync();
    }

    private async void OnLpnTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is Guid id && LpnRows.Any(x => x.Id == id && x.CanOpen))
            await OpenLpnPageAsync(id);
    }

    private async Task OpenLpnPageAsync(Guid id)
    {
        if (_order is null || _busy || _process.HasPendingCommand
            || !LpnRows.Any(x => x.Id == id && x.CanOpen)) return;
        var page = _services.GetRequiredService<ReceivingOrderLicensePlateNumberPage>();
        page.Show(_order.Id, id, _process);
        await Navigation.PushAsync(page);
    }

    private async Task OpenSkuDetailsAsync(Guid skuId)
    {
        if (_order is null || _busy || _process.HasPendingCommand) return;
        var page = _services.GetRequiredService<ReceivingOrderSkuDistributionPage>();
        page.Show(_order.Id, skuId, _process);
        await Navigation.PushAsync(page);
    }

    private async void OnSkuClicked(object? sender, EventArgs e)
    {
        if (_order is null || _busy || _process.HasPendingCommand) return;
        var page = _services.GetRequiredService<ReceivingOrderSkuPage>();
        page.Show(_order, _process);
        await Navigation.PushAsync(page);
        Navigation.RemovePage(this);
    }

    private async void OnSourcesClicked(object? sender, EventArgs e)
    {
        if (_order is null || _busy || _process.HasPendingCommand) return;
        var page = _services.GetRequiredService<ReceivingOrderSourcesPage>();
        page.Show(_order, _process);
        await Navigation.PushAsync(page);
        Navigation.RemovePage(this);
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
        finally { SetBusy(false); await UpdateCameraAsync(); }
    }

    private async void OnRetryClicked(object? sender, EventArgs e)
    {
        if (_busy || !_process.HasPendingCommand) return;
        SetBusy(true);
        Guid? openedId = null;
        var completing = _process.PendingLabel == "Завершить ордер";
        var addingSku = _process.PendingLabel == "Добавить SKU";
        var succeeded = false;
        try
        {
            var opening = _process.PendingLabel == "Открыть LPN";
            Apply(await _process.RetryAsync());
            if (opening) openedId = _process.LastResourceId;
            ErrorLabel.Text = string.Empty;
            succeeded = true;
        }
        catch (MobileApiException ex)
        {
            _pendingSkuId = null;
            ErrorLabel.Text = ex.Message;
        }
        catch (HttpRequestException) { ErrorLabel.Text = "Ответ не подтверждён. Повторите ту же команду."; }
        finally { SetBusy(false); await UpdateCameraAsync(); }
        if (succeeded && completing) await Navigation.PopAsync();
        else if (succeeded && openedId is Guid id) await OpenLpnPageAsync(id);
        else if (succeeded && addingSku && _pendingSkuId is Guid skuId)
        {
            _pendingSkuId = null;
            await OpenSkuDetailsAsync(skuId);
        }
    }

    private void UpdateRecovery()
    {
        RetryButton.IsVisible = _process.HasPendingCommand;
        RetryButton.Text = $"Повторить: {_process.PendingLabel}";
        JoinButton.IsEnabled = !_busy && !_process.HasPendingCommand;
        CompleteButton.IsEnabled = !_busy && !_process.HasPendingCommand
            && _order is { MayCompleteOrder: true } order && !HasOpenLpn(order);
        SkuButton.IsEnabled = !_busy && !_process.HasPendingCommand;
        SourcesButton.IsEnabled = !_busy && !_process.HasPendingCommand;
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

    private static bool HasOpenLpn(MobileReceivingWorkOrderResponse order) =>
        order.Lpns.Any(x => x.Status == MobileLicensePlateNumberStatus.Open);

    private static string Format(decimal? quantity) => quantity?.ToString("0.###") ?? "?";
}

public sealed record ReceivingWorkLpnRow(Guid Id, string Code,
    string StatusGlyph, string StatusDescription, Color StatusColor, string SkuName,
    string PlanText, string FactText, Color FactColor, string QuantityText,
    string ExpirationText, bool ShowsExpiration, bool ShowsQuantities,
    bool IsEditable, bool IsClosed, bool IsUnchecked, bool IsMatched, bool IsDifferent,
    string FactDescription)
{
    public FontAttributes StatusFontAttributes =>
        ReceivingStatusSymbols.FontForGlyph(StatusGlyph);
    public bool CanOpen => IsEditable || IsClosed;
    public bool IsReadOnly => !CanOpen;
    public string OpenHint => IsClosed ? "Нажмите, чтобы посмотреть LPN"
        : "Нажмите, чтобы изменить LPN";
}

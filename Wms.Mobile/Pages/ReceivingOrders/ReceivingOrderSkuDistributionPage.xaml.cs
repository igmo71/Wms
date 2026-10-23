using Microsoft.Extensions.DependencyInjection;
using Wms.Contracts.Mobile.V1;
using Wms.Mobile.Scanning;
using Wms.Mobile.Services;

namespace Wms.Mobile;

public partial class ReceivingOrderSkuDistributionPage : ContentPage
{
    private readonly MobileReceivingOrderClient _orders;
    private readonly IOperationalBarcodeScanner _scanner;
    private readonly IServiceProvider _services;
    private ReceivingOrderReceivingProcess _process = null!;
    private MobileReceivingWorkOrderResponse? _order;
    private Guid _orderId;
    private Guid _skuId;
    private bool _visible;
    private bool _busy;
    private long _scanGeneration;

    public ReceivingOrderSkuDistributionPage(MobileReceivingOrderClient orders,
        IOperationalBarcodeScanner scanner, IServiceProvider services)
    {
        InitializeComponent();
        _orders = orders;
        _scanner = scanner;
        _services = services;
        CameraScannerView.Configure(scanner);
    }

    public IReadOnlyList<ReceivingOrderSkuLpnRow> LpnRows { get; private set; } = [];

    public void Show(Guid orderId, Guid skuId, ReceivingOrderReceivingProcess process)
    {
        _orderId = orderId;
        _skuId = skuId;
        _process = process;
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
        SkuRefreshView.IsRefreshing = false;
    }

    private async Task RefreshAsync()
    {
        if (_busy) return;
        SetBusy(true);
        try
        {
            Apply(await _orders.GetAsync(_orderId));
            ErrorLabel.Text = string.Empty;
        }
        catch (MobileApiException ex) { ErrorLabel.Text = ex.Message; }
        catch (HttpRequestException) { ErrorLabel.Text = "Не удалось обновить SKU."; }
        catch (InvalidOperationException ex) { ErrorLabel.Text = ex.Message; }
        finally { SetBusy(false); await UpdateCameraAsync(); }
    }

    private void Apply(MobileReceivingWorkOrderResponse order)
    {
        var item = order.Items.SingleOrDefault(x => x.SkuId == _skuId)
            ?? throw new InvalidOperationException("SKU отсутствует в ордере.");
        _order = order;
        SkuNameLabel.Text = item.SkuName ?? item.SkuId.ToString();
        SkuCodeLabel.Text = item.SkuCode;
        PlanLabel.Text = item.PlanQuantity?.ToString("0.###") ?? "?";
        FactLabel.Text = ReceivingStatusSymbols.FactText(item.PlanQuantity, item.FactQuantity);
        FactLabel.TextColor = ReceivingStatusSymbols.FactColor(item.PlanQuantity, item.FactQuantity);
        SemanticProperties.SetDescription(FactLabel,
            ReceivingStatusSymbols.FactDescription(item.PlanQuantity, item.FactQuantity));
        UnitLabel.Text = item.UnitOfMeasure ?? "—";
        LpnRows = order.Lpns.Where(x => x.Status is not
                (MobileLicensePlateNumberStatus.Cancelled or MobileLicensePlateNumberStatus.Deleted))
                .Select(x => new { Lpn = x, Item = x.Items.SingleOrDefault(y => y.SkuId == _skuId) })
                .Where(x => x.Item is not null)
                .Select(x =>
                {
                    var status = ReceivingStatusSymbols.ForLpn(x.Lpn.Status);
                    return new ReceivingOrderSkuLpnRow(x.Lpn.Id, x.Lpn.Code,
                        status.Glyph, $"Статус LPN: {status.Description}", status.TextColor,
                        $"Ответственный: {x.Lpn.ResponsibleUserName ?? "Имя не указано"}",
                        $"{x.Item!.Quantity:0.###} {item.UnitOfMeasure ?? "—"}",
                        x.Lpn.Status == MobileLicensePlateNumberStatus.Open
                            && order.IsParticipant,
                        x.Lpn.Status == MobileLicensePlateNumberStatus.Closed);
                })
                .ToArray();
        OnPropertyChanged(nameof(LpnRows));
        var canStartLpn = order.IsParticipant
            && order.Status == MobileReceivingOrderStatus.InReceiving;
        ScanHintLabel.IsVisible = canStartLpn || LpnRows.Any(x => x.CanOpen);
        ScanHintLabel.Text = canStartLpn
            ? "Сканируйте выданный LPN для этого SKU или LPN из списка для просмотра."
            : "Сканируйте LPN из списка, чтобы открыть его.";
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
        code = code.Trim();
        if (!code.StartsWith("LPN", StringComparison.OrdinalIgnoreCase))
        {
            ErrorLabel.Text = "Сканируйте этикетку LPN.";
            await UpdateCameraAsync();
            return;
        }
        var listedLpn = LpnRows.FirstOrDefault(x => x.CanOpen
            && string.Equals(x.Code, code, StringComparison.OrdinalIgnoreCase));
        if (listedLpn is not null)
        {
            await OpenLpnDetailsAsync(listedLpn.LicensePlateNumberId);
            return;
        }
        if (_order.Status != MobileReceivingOrderStatus.InReceiving)
        {
            ErrorLabel.Text = "Ордер сейчас не находится в приёмке.";
            await UpdateCameraAsync();
            return;
        }
        if (!_order.IsParticipant)
        {
            ErrorLabel.Text = "Сначала присоединитесь к приёмке.";
            await UpdateCameraAsync();
            return;
        }
        var existing = _order.Lpns.FirstOrDefault(x =>
            string.Equals(x.Code, code, StringComparison.OrdinalIgnoreCase));
        if (existing is not null && !CanPlaceSku(existing))
        {
            ErrorLabel.Text = "Этот LPN недоступен для выбранного SKU.";
            await UpdateCameraAsync();
            return;
        }

        SetBusy(true);
        ErrorLabel.Text = string.Empty;
        try
        {
            Apply(await _process.OpenLpnAsync(_orderId, code));
            if (_process.LastResourceId is Guid lpnId)
                await OpenScannedLpnAsync(lpnId);
        }
        catch (MobileApiException ex) { ErrorLabel.Text = ex.Message; }
        catch (HttpRequestException) { ErrorLabel.Text = "Ответ не подтверждён. Повторите ту же команду."; }
        catch (InvalidOperationException ex) { ErrorLabel.Text = ex.Message; }
        finally { SetBusy(false); await UpdateCameraAsync(); }
    }

    private bool CanPlaceSku(MobileReceivingWorkLpnResponse lpn) =>
        lpn.Status == MobileLicensePlateNumberStatus.Open
        && (lpn.Items.Count == 0 || lpn.Items.All(x => x.SkuId == _skuId));

    private async Task OpenScannedLpnAsync(Guid lpnId)
    {
        var lpn = _order?.Lpns.SingleOrDefault(x => x.Id == lpnId);
        if (lpn is null || !CanPlaceSku(lpn))
        {
            ErrorLabel.Text = "Этот LPN недоступен для выбранного SKU.";
            return;
        }
        var page = _services.GetRequiredService<ReceivingOrderLicensePlateNumberPage>();
        page.Show(_orderId, lpnId, _process, _skuId);
        await Navigation.PushAsync(page);
    }

    private async void OnRetryClicked(object? sender, EventArgs e)
    {
        if (_busy || !_process.HasPendingCommand) return;
        SetBusy(true);
        try
        {
            Apply(await _process.RetryAsync());
            ErrorLabel.Text = string.Empty;
            if (_process.LastResourceId is Guid lpnId)
                await OpenScannedLpnAsync(lpnId);
        }
        catch (MobileApiException ex) { ErrorLabel.Text = ex.Message; }
        catch (HttpRequestException) { ErrorLabel.Text = "Ответ не подтверждён. Повторите ту же команду."; }
        catch (InvalidOperationException ex) { ErrorLabel.Text = ex.Message; }
        finally { SetBusy(false); await UpdateCameraAsync(); }
    }

    private void UpdateRecovery()
    {
        RetryButton.IsVisible = _process?.HasPendingCommand == true;
        RetryButton.Text = $"Повторить: {_process?.PendingLabel}";
        NavigationPage.SetHasBackButton(this, !_busy && _process?.HasPendingCommand != true);
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
            && ((_order is { IsParticipant: true, Status: MobileReceivingOrderStatus.InReceiving })
                || LpnRows.Any(x => x.CanOpen))
            && _scanner.ActiveSource == BarcodeScanSource.Camera)
            await CameraScannerView.StartAsync();
        else CameraScannerView.Stop();
    }

    private async void OnLpnTapped(object? sender, TappedEventArgs e)
    {
        if (_busy || _process.HasPendingCommand || e.Parameter is not Guid id
            || !LpnRows.Any(x => x.LicensePlateNumberId == id && x.CanOpen)) return;
        await OpenLpnDetailsAsync(id);
    }

    private async Task OpenLpnDetailsAsync(Guid id)
    {
        var page = _services.GetRequiredService<ReceivingOrderLicensePlateNumberPage>();
        page.Show(_orderId, id, _process);
        await Navigation.PushAsync(page);
    }
}

public sealed record ReceivingOrderSkuLpnRow(Guid LicensePlateNumberId,
    string Code, string StatusGlyph, string StatusDescription, Color StatusColor,
    string ResponsibleUserName, string QuantityText, bool IsEditable, bool IsClosed)
{
    public FontAttributes StatusFontAttributes =>
        ReceivingStatusSymbols.FontForGlyph(StatusGlyph);
    public bool CanOpen => IsEditable || IsClosed;
    public bool IsReadOnly => !CanOpen;
}

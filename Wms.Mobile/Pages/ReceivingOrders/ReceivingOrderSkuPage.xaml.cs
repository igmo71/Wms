using Microsoft.Extensions.DependencyInjection;
using Wms.Contracts.Mobile.V1;
using Wms.Mobile.Scanning;
using Wms.Mobile.Services;

namespace Wms.Mobile;

public partial class ReceivingOrderSkuPage : ContentPage
{
    private ReceivingOrderReceivingProcess _process;
    private readonly MobileReceivingOrderClient _orders;
    private readonly IOperationalBarcodeScanner _scanner;
    private readonly IServiceProvider _services;
    private MobileReceivingWorkOrderResponse? _order;
    private bool _visible;
    private bool _busy;
    private long _scanGeneration;

    public ReceivingOrderSkuPage(ReceivingOrderReceivingProcess process,
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

    public IReadOnlyList<ReceivingWorkItemRow> ItemRows { get; private set; } = [];

    public void Show(MobileReceivingWorkOrderResponse order,
        ReceivingOrderReceivingProcess? process = null)
    {
        if (process is not null) _process = process;
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
        var status = order.Status switch
        {
            MobileReceivingOrderStatus.ReadyForReceiving => "К приёмке",
            MobileReceivingOrderStatus.InReceiving => "В работе",
            MobileReceivingOrderStatus.Received => "Принят",
            _ => order.Status.ToString()
        };
        StatusLabel.Text = order.ReceivingLocationId is Guid id
            ? $"{status} · Место: {order.ReceivingLocationAddress ?? id.ToString()}"
            : $"{status} · Место не выбрано";
        JoinButton.Text = order.Status == MobileReceivingOrderStatus.ReadyForReceiving
            ? "Начать" : "Присоединиться";
        JoinButton.IsVisible = !order.IsParticipant
            && order.Status is (MobileReceivingOrderStatus.ReadyForReceiving
                or MobileReceivingOrderStatus.InReceiving);
        ScanHintLabel.Text = order.IsParticipant
            ? "Сканируйте выданную этикетку LPN. SKU сканируется внутри LPN."
            : "Присоединитесь к циклу приёмки, чтобы открыть LPN.";
        ItemRows = order.Items.Where(item => item.IsCurrent)
            .OrderBy(DisplayName, StringComparer.Create(
                System.Globalization.CultureInfo.GetCultureInfo("ru-RU"), ignoreCase: true))
            .ThenBy(item => item.SkuCode ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.SkuId)
            .Select(item =>
            {
                var matched = item.PlanQuantity is decimal plan
                    && item.FactQuantity is decimal fact && plan == fact;
                var different = item.PlanQuantity is decimal expected
                    && item.FactQuantity is decimal actual && expected != actual;
                var activeLpn = order.Lpns.Any(x =>
                    (x.Status is MobileLicensePlateNumberStatus.Open or MobileLicensePlateNumberStatus.Closed)
                    && x.Items.Any(y => y.SkuId == item.SkuId));
                var unit = string.IsNullOrWhiteSpace(item.UnitOfMeasure) ? "—" : item.UnitOfMeasure;
                return new ReceivingWorkItemRow(item.SkuId, DisplayName(item),
                    $"План: {Format(item.PlanQuantity)} {unit}",
                    $"Факт: {Format(item.FactQuantity)}",
                    order.IsParticipant && !activeLpn && item.FactQuantity is null,
                    item.FactQuantity is null, matched, different);
            }).ToArray();
        OnPropertyChanged(nameof(ItemRows));
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
        await ExecuteAsync(async () => Apply(await _process.OpenLpnAsync(_order.Id, code.Trim())));
        if (!_process.HasPendingCommand && _process.LastResourceId is Guid id)
            await OpenLpnPageAsync(id);
    }

    private async void OnJoinClicked(object? sender, EventArgs e)
    {
        if (_order is null || _busy || _process.HasPendingCommand) return;
        var page = _services.GetRequiredService<ReceivingOrderJoinPage>();
        page.Show(_order, _process);
        await Navigation.PushAsync(page);
    }

    private async void OnZeroClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: Guid skuId } || _order is null) return;
        if (!await DisplayAlertAsync("Проверено: 0", "Подтвердить отсутствие этого SKU?", "Да", "Нет")) return;
        await ExecuteAsync(async () => Apply(await _process.ConfirmZeroAsync(_order.Id, skuId)));
    }

    private async Task OpenLpnPageAsync(Guid id)
    {
        if (_order is null || _busy || _process.HasPendingCommand) return;
        var page = _services.GetRequiredService<ReceivingOrderLicensePlateNumberPage>();
        page.Show(_order.Id, id, _process);
        await Navigation.PushAsync(page);
    }

    private async void OnSkuTapped(object? sender, TappedEventArgs e)
    {
        if (_order is null || _busy || _process.HasPendingCommand
            || e.Parameter is not Guid skuId) return;
        var page = _services.GetRequiredService<ReceivingOrderSkuDistributionPage>();
        page.Show(_order.Id, skuId, _process);
        await Navigation.PushAsync(page);
    }

    private async void OnLpnsClicked(object? sender, EventArgs e)
    {
        if (_order is null || _busy || _process.HasPendingCommand) return;
        var page = _services.GetRequiredService<ReceivingOrderLpnListPage>();
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
        var succeeded = false;
        try
        {
            var opening = _process.PendingLabel == "Открыть LPN";
            Apply(await _process.RetryAsync());
            if (opening) openedId = _process.LastResourceId;
            ErrorLabel.Text = string.Empty;
            succeeded = true;
        }
        catch (MobileApiException ex) { ErrorLabel.Text = ex.Message; }
        catch (HttpRequestException) { ErrorLabel.Text = "Ответ не подтверждён. Повторите ту же команду."; }
        finally { SetBusy(false); await UpdateCameraAsync(); }
        if (succeeded && openedId is Guid id) await OpenLpnPageAsync(id);
    }

    private void UpdateRecovery()
    {
        RetryButton.IsVisible = _process.HasPendingCommand;
        RetryButton.Text = $"Повторить: {_process.PendingLabel}";
        JoinButton.IsEnabled = !_busy && !_process.HasPendingCommand;
        LpnsButton.IsEnabled = !_busy && !_process.HasPendingCommand;
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

    private static string DisplayName(MobileReceivingWorkItemResponse item) =>
        !string.IsNullOrWhiteSpace(item.SkuName) ? item.SkuName
        : !string.IsNullOrWhiteSpace(item.SkuCode) ? item.SkuCode
        : item.SkuId.ToString();

    private static string Format(decimal? value) => value?.ToString("0.###") ?? "?";
}

public sealed record ReceivingWorkItemRow(Guid SkuId, string Name,
    string PlanText, string FactText, bool CanConfirmZero,
    bool IsUnchecked, bool IsMatched, bool IsDifferent);

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
    private Guid? _pendingSkuId;
    private Guid? _barcodeSkuId;
    private string? _barcodeValue;
    private bool _barcodePanelOpen;
    private bool _visible;
    private bool _busy;
    private bool _useSuppliedOrderOnNextAppearance;
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
        BarcodeCameraScannerView.Configure(scanner);
    }

    public IReadOnlyList<ReceivingWorkItemRow> ItemRows { get; private set; } = [];

    public void Show(MobileReceivingWorkOrderResponse order,
        ReceivingOrderReceivingProcess? process = null)
    {
        if (process is not null) _process = process;
        Apply(order);
        _useSuppliedOrderOnNextAppearance = true;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _visible = true;
        _scanner.ScanReceived += OnScanReceived;
        if (_useSuppliedOrderOnNextAppearance)
            _useSuppliedOrderOnNextAppearance = false;
        else
            await RefreshAsync();
        await UpdateCameraAsync();
    }

    protected override void OnDisappearing()
    {
        _visible = false;
        Interlocked.Increment(ref _scanGeneration);
        _scanner.ScanReceived -= OnScanReceived;
        CameraScannerView.Stop();
        BarcodeCameraScannerView.Stop();
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
                    $"{Format(item.PlanQuantity)} {unit}",
                    ReceivingStatusSymbols.FactText(item.PlanQuantity, item.FactQuantity),
                    ReceivingStatusSymbols.FactDescription(item.PlanQuantity, item.FactQuantity),
                    ReceivingStatusSymbols.FactColor(item.PlanQuantity, item.FactQuantity),
                    order.IsParticipant && !activeLpn && item.FactQuantity is null,
                    item.FactQuantity is null, matched, different,
                    order.IsParticipant && order.Status == MobileReceivingOrderStatus.InReceiving,
                    item.HasBarcode ? 1d : 0.35d,
                    item.HasBarcode
                        ? "Добавить штрихкод SKU. Штрихкоды уже есть."
                        : "Добавить штрихкод SKU. Штрихкодов пока нет.");
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
        var scannedCode = code.Trim();
        if (_barcodePanelOpen)
        {
            if (scannedCode.Length == 0)
            {
                ErrorLabel.Text = "Штрихкод пуст. Отсканируйте товар ещё раз.";
                return;
            }
            _barcodeValue = scannedCode;
            BarcodeValueLabel.Text = scannedCode;
            BarcodeRescanButton.IsVisible = true;
            ErrorLabel.Text = string.Empty;
            UpdateRecovery();
            await UpdateCameraAsync();
            return;
        }
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
        await ExecuteAsync(async () => Apply(await _process.CompleteOrderAsync(current.Id)));
    }

    private async void OnZeroClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: Guid skuId } || _order is null) return;
        if (!await DisplayAlertAsync("Проверено: 0", "Подтвердить отсутствие этого SKU?", "Да", "Нет")) return;
        await ExecuteAsync(async () => Apply(await _process.ConfirmZeroAsync(_order.Id, skuId)));
    }

    private async void OnBarcodeClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: Guid skuId }
            || _order is not { IsParticipant: true,
                Status: MobileReceivingOrderStatus.InReceiving }
            || _busy || _process.HasPendingCommand || _barcodePanelOpen)
            return;
        var row = ItemRows.FirstOrDefault(x => x.SkuId == skuId);
        if (row is null || !row.CanAddBarcode) return;
        Interlocked.Increment(ref _scanGeneration);
        _barcodeSkuId = skuId;
        _barcodeValue = null;
        _barcodePanelOpen = true;
        BarcodeSkuLabel.Text = row.Name;
        BarcodeValueLabel.Text = "Штрихкод ещё не считан";
        BarcodeRescanButton.IsVisible = false;
        BarcodePanel.IsVisible = true;
        ItemList.IsVisible = false;
        ErrorLabel.Text = string.Empty;
        UpdateRecovery();
        await UpdateCameraAsync();
    }

    private async void OnBarcodeCancelClicked(object? sender, EventArgs e)
    {
        if (_busy || _process.HasPendingCommand) return;
        await CloseBarcodePanelAsync();
    }

    private async void OnBarcodeRescanClicked(object? sender, EventArgs e)
    {
        if (_busy || _process.HasPendingCommand) return;
        _barcodeValue = null;
        BarcodeValueLabel.Text = "Штрихкод ещё не считан";
        BarcodeRescanButton.IsVisible = false;
        ErrorLabel.Text = string.Empty;
        UpdateRecovery();
        await UpdateCameraAsync();
    }

    private async void OnBarcodeSaveClicked(object? sender, EventArgs e)
    {
        if (_order is null || _barcodeSkuId is not Guid skuId
            || string.IsNullOrWhiteSpace(_barcodeValue)
            || _busy || _process.HasPendingCommand) return;
        var orderId = _order.Id;
        var barcode = _barcodeValue;
        await ExecuteAsync(async () =>
        {
            Apply(await _process.AddSkuBarcodeAsync(orderId, skuId, barcode));
            await CloseBarcodePanelAsync();
        });
    }

    private async Task CloseBarcodePanelAsync()
    {
        if (!_barcodePanelOpen) return;
        Interlocked.Increment(ref _scanGeneration);
        _barcodePanelOpen = false;
        _barcodeSkuId = null;
        _barcodeValue = null;
        BarcodeCameraScannerView.Stop();
        BarcodePanel.IsVisible = false;
        ItemList.IsVisible = true;
        ErrorLabel.Text = string.Empty;
        UpdateRecovery();
        await UpdateCameraAsync();
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
        await OpenSkuDetailsAsync(skuId);
    }

    private async Task OpenSkuDetailsAsync(Guid skuId)
    {
        if (_order is null || _busy || _process.HasPendingCommand) return;
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
        var addingSku = _process.PendingLabel == "Добавить SKU";
        var addingBarcode = _process.PendingLabel == "Добавить штрихкод";
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
        if (succeeded && openedId is Guid id) await OpenLpnPageAsync(id);
        else if (succeeded && addingSku && _pendingSkuId is Guid skuId)
        {
            _pendingSkuId = null;
            await OpenSkuDetailsAsync(skuId);
        }
        else if (succeeded && addingBarcode) await CloseBarcodePanelAsync();
    }

    private void UpdateRecovery()
    {
        RetryButton.IsVisible = _process.HasPendingCommand;
        RetryButton.Text = $"Повторить: {_process.PendingLabel}";
        JoinButton.IsVisible = !_barcodePanelOpen && _order is { IsParticipant: false,
            Status: MobileReceivingOrderStatus.ReadyForReceiving
                or MobileReceivingOrderStatus.InReceiving };
        JoinButton.IsEnabled = !_busy && !_process.HasPendingCommand;
        CompleteButton.IsVisible = !_barcodePanelOpen && _order?.MayCompleteOrder == true;
        CompleteButton.IsEnabled = !_busy && !_process.HasPendingCommand
            && _order is { MayCompleteOrder: true } order && !HasOpenLpn(order);
        SectionButtons.IsVisible = !_barcodePanelOpen;
        LpnsButton.IsEnabled = !_busy && !_process.HasPendingCommand;
        SourcesButton.IsEnabled = !_busy && !_process.HasPendingCommand;
        BarcodeSaveButton.IsEnabled = _barcodePanelOpen && !_busy
            && !_process.HasPendingCommand && !string.IsNullOrEmpty(_barcodeValue);
        BarcodeCancelButton.IsEnabled = _barcodePanelOpen && !_busy
            && !_process.HasPendingCommand;
        BarcodeRescanButton.IsEnabled = BarcodeCancelButton.IsEnabled;
        NavigationPage.SetHasBackButton(this, !_busy && !_process.HasPendingCommand);
    }

    protected override bool OnBackButtonPressed()
    {
        if (_barcodePanelOpen && !_busy && !_process.HasPendingCommand)
        {
            _ = CloseBarcodePanelAsync();
            return true;
        }
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
        var cameraReady = _visible && !_busy && !_process.HasPendingCommand
            && _scanner.ActiveSource == BarcodeScanSource.Camera;
        if (_barcodePanelOpen)
        {
            CameraScannerView.Stop();
            if (cameraReady && _barcodeValue is null)
                await BarcodeCameraScannerView.StartAsync();
            else BarcodeCameraScannerView.Stop();
        }
        else
        {
            BarcodeCameraScannerView.Stop();
            if (cameraReady) await CameraScannerView.StartAsync();
            else CameraScannerView.Stop();
        }
    }

    private static string DisplayName(MobileReceivingWorkItemResponse item) =>
        !string.IsNullOrWhiteSpace(item.SkuName) ? item.SkuName
        : !string.IsNullOrWhiteSpace(item.SkuCode) ? item.SkuCode
        : item.SkuId.ToString();

    private static string Format(decimal? value) => value?.ToString("0.###") ?? "?";

    private static bool HasOpenLpn(MobileReceivingWorkOrderResponse order) =>
        order.Lpns.Any(x => x.Status == MobileLicensePlateNumberStatus.Open);
}

public sealed record ReceivingWorkItemRow(Guid SkuId, string Name,
    string PlanText, string FactText, string FactDescription, Color FactColor, bool CanConfirmZero,
    bool IsUnchecked, bool IsMatched, bool IsDifferent, bool CanAddBarcode,
    double BarcodeOpacity, string BarcodeDescription);

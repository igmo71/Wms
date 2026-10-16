using Microsoft.Extensions.DependencyInjection;
using Wms.Contracts.Mobile.V1;
using Wms.Mobile.Scanning;
using Wms.Mobile.Services;

namespace Wms.Mobile;

public partial class ReceivingOrderLicensePlateNumberPage : ContentPage
{
    private readonly MobileReceivingOrderClient _orders;
    private readonly IOperationalBarcodeScanner _scanner;
    private readonly IServiceProvider _services;
    private ReceivingOrderReceivingProcess _process = null!;
    private MobileReceivingWorkOrderResponse? _order;
    private Guid _orderId;
    private Guid _lpnId;
    private Guid? _pendingSkuId;
    private bool _catalogSearch;
    private bool _visible;
    private bool _busy;
    private long _scanGeneration;

    public ReceivingOrderLicensePlateNumberPage(MobileReceivingOrderClient orders,
        IOperationalBarcodeScanner scanner, IServiceProvider services)
    {
        InitializeComponent();
        _orders = orders;
        _scanner = scanner;
        _services = services;
        BindingContext = this;
        CameraScannerView.Configure(scanner);
    }

    public IReadOnlyList<ReceivingLpnItemRow> ItemRows { get; private set; } = [];
    public IReadOnlyList<ReceivingSkuChoiceRow> CandidateRows { get; private set; } = [];
    private MobileReceivingWorkLpnResponse? Lpn =>
        _order?.Lpns.SingleOrDefault(x => x.Id == _lpnId);

    public void Show(Guid orderId, Guid lpnId, ReceivingOrderReceivingProcess process)
    {
        _orderId = orderId;
        _lpnId = lpnId;
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
        LpnRefreshView.IsRefreshing = false;
    }

    private async Task RefreshAsync()
    {
        if (_busy || _orderId == Guid.Empty) return;
        SetBusy(true);
        try { Apply(await _orders.GetAsync(_orderId)); }
        catch (MobileApiException ex) { ErrorLabel.Text = ex.Message; }
        catch (HttpRequestException) { ErrorLabel.Text = "Не удалось обновить LPN."; }
        finally { SetBusy(false); await UpdateCameraAsync(); }
    }

    private void Apply(MobileReceivingWorkOrderResponse order)
    {
        _order = order;
        var lpn = Lpn;
        if (lpn is null)
        {
            ErrorLabel.Text = "LPN больше не входит в этот ордер.";
            return;
        }
        CodeLabel.Text = lpn.Code;
        StatusLabel.Text = lpn.Status switch
        {
            MobileLicensePlateNumberStatus.Open => "Открыта",
            MobileLicensePlateNumberStatus.Closed => "Закрыта: приход проведён",
            MobileLicensePlateNumberStatus.Cancelled => "Отменена",
            MobileLicensePlateNumberStatus.Deleted => "Удалена",
            _ => lpn.Status.ToString()
        };
        ItemRows = lpn.Items.Select(x => new ReceivingLpnItemRow(x.SkuId,
            x.SkuName ?? x.SkuId.ToString(),
            $"{x.Quantity:0.###} {x.UnitOfMeasure ?? "—"} · срок: {x.ExpirationDate?.ToString("dd.MM.yyyy") ?? "не указан"} · размещено: {x.PlacedQuantity:0.###}")).ToArray();
        OnPropertyChanged(nameof(ItemRows));
        var editable = lpn.Status == MobileLicensePlateNumberStatus.Open
            && order.IsParticipant;
        SearchButton.IsVisible = editable;
        CloseButton.IsVisible = editable && lpn.Items.Any(x => x.Quantity > 0);
        CancelButton.IsVisible = editable;
        PutawayButton.IsVisible = lpn.Status == MobileLicensePlateNumberStatus.Closed
            && lpn.Items.Any(x => x.RemainingQuantity > 0);
        if (!editable) SearchPanel.IsVisible = false;
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

    private async Task HandleScanAsync(string barcode)
    {
        if (!_visible || _busy || _process.HasPendingCommand || Lpn is null) return;
        if (barcode.Trim().StartsWith("LPN", StringComparison.OrdinalIgnoreCase))
        {
            await ExecuteAsync(async () =>
            {
                Apply(await _process.OpenLpnAsync(_orderId, barcode.Trim()));
                if (_process.LastResourceId is Guid id) _lpnId = id;
            });
            await RefreshAsync();
            return;
        }
        if (Lpn.Status != MobileLicensePlateNumberStatus.Open)
        {
            ErrorLabel.Text = "Закрытую LPN нельзя изменить. Для размещения откройте экран размещения.";
            return;
        }
        SetBusy(true);
        try
        {
            var sku = await _orders.ResolveSkuAsync(_orderId, barcode);
            SetBusy(false);
            await SelectSkuAsync(sku.SkuId, sku.IsInOrder);
        }
        catch (MobileApiException ex) { ErrorLabel.Text = ex.Message; }
        catch (HttpRequestException) { ErrorLabel.Text = "Не удалось определить SKU. Повторите сканирование."; }
        finally { SetBusy(false); await UpdateCameraAsync(); }
    }

    private async void OnItemTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is Guid skuId && Lpn?.Status == MobileLicensePlateNumberStatus.Open)
            await SelectSkuAsync(skuId, true);
    }

    private async void OnManualSearchClicked(object? sender, EventArgs e)
    {
        if (_busy || _process.HasPendingCommand) return;
        SearchPanel.IsVisible = !SearchPanel.IsVisible;
        SearchButton.Text = SearchPanel.IsVisible ? "Скрыть поиск" : "Выбрать SKU вручную";
        if (SearchPanel.IsVisible) await SearchAsync();
    }

    private async void OnScopeClicked(object? sender, EventArgs e)
    {
        _catalogSearch = !_catalogSearch;
        ScopeButton.Text = _catalogSearch ? "Поиск: весь справочник" : "Поиск: состав ордера";
        await SearchAsync();
    }

    private async void OnSearchClicked(object? sender, EventArgs e) => await SearchAsync();

    private async Task SearchAsync()
    {
        if (_busy || _process.HasPendingCommand || Lpn?.Status != MobileLicensePlateNumberStatus.Open) return;
        SetBusy(true);
        try
        {
            var results = await _orders.SearchSkuAsync(_orderId, SkuSearch.Text, _catalogSearch);
            CandidateRows = results.Select(x => new ReceivingSkuChoiceRow(x.SkuId,
                $"{x.Name ?? x.SkuId.ToString()} · {x.Code}",
                $"{(x.IsInOrder ? "План" : "Вне плана")}: {x.CurrentPlanQuantity?.ToString("0.###") ?? "?"}",
                x.IsInOrder)).ToArray();
            OnPropertyChanged(nameof(CandidateRows));
        }
        catch (MobileApiException ex) { ErrorLabel.Text = ex.Message; }
        catch (HttpRequestException) { ErrorLabel.Text = "Не удалось найти SKU."; }
        finally { SetBusy(false); }
    }

    private async void OnCandidateTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is not Guid id || _busy || _process.HasPendingCommand) return;
        var candidate = CandidateRows.SingleOrDefault(x => x.SkuId == id);
        if (candidate is not null) await SelectSkuAsync(id, candidate.IsInOrder);
    }

    private async Task SelectSkuAsync(Guid skuId, bool inOrder)
    {
        if (_busy || _process.HasPendingCommand || Lpn?.Status != MobileLicensePlateNumberStatus.Open) return;
        if (!inOrder)
        {
            if (!await DisplayAlertAsync("SKU вне плана", "Добавить SKU в ордер с планом 0?", "Добавить", "Назад")) return;
            _pendingSkuId = skuId;
            await ExecuteAsync(async () => Apply(await _process.AddSkuAsync(_orderId, skuId)));
            if (_process.HasPendingCommand || _pendingSkuId is null) return;
            _pendingSkuId = null;
        }
        var page = _services.GetRequiredService<ReceivingOrderLicensePlateNumberQuantityPage>();
        page.Show(_orderId, _lpnId, skuId, _process);
        await Navigation.PushAsync(page);
    }

    private async void OnCloseClicked(object? sender, EventArgs e)
    {
        if (Lpn is not { Status: MobileLicensePlateNumberStatus.Open } lpn || _busy) return;
        if (lpn.Items.Count == 0 || lpn.Items.All(x => x.Quantity <= 0)) return;
        var expired = lpn.Items.Any(x => x.ExpirationDate is DateOnly date
            && date < DateOnly.FromDateTime(DateTime.UtcNow));
        if (expired && !await DisplayAlertAsync("Прошедший срок",
                "Срок годности прошёл. Подтвердить закрытие LPN?", "Подтвердить", "Назад")) return;
        if (!await DisplayAlertAsync("Провести приход", $"Закрыть {lpn.Code} и провести количество?", "Да", "Нет")) return;
        await ExecuteAsync(async () => Apply(await _process.CloseLpnAsync(_orderId, _lpnId, expired, null)),
            refreshOnConflict: true);
        if (_process.HasPendingCommand || _process.LastCloseResult is null) return;
        var result = _process.LastCloseResult;
        if (!result.SourceCheckSucceeded)
            await DisplayAlertAsync("Сверка 1С", result.SourceCheckError ?? "План не подтверждён.", "Понятно");
        await Navigation.PopAsync();
    }

    private async void OnCancelClicked(object? sender, EventArgs e)
    {
        if (Lpn is not { Status: MobileLicensePlateNumberStatus.Open } lpn || _busy) return;
        if (!await DisplayAlertAsync("Отменить LPN", $"Отменить {lpn.Code} без прихода?", "Отменить", "Назад")) return;
        await ExecuteAsync(async () => Apply(await _process.CancelLpnAsync(_orderId, _lpnId)));
        if (!_process.HasPendingCommand && Lpn?.Status == MobileLicensePlateNumberStatus.Cancelled)
            await Navigation.PopAsync();
    }

    private async void OnPutawayClicked(object? sender, EventArgs e)
    {
        if (Lpn is not { Status: MobileLicensePlateNumberStatus.Closed } lpn || _busy) return;
        var page = _services.GetRequiredService<LicensePlateNumberPutawayDetailsPage>();
        page.Show(lpn);
        await Navigation.PushAsync(page);
    }

    private async Task ExecuteAsync(Func<Task> action, bool refreshOnConflict = false)
    {
        if (_busy || _process.HasPendingCommand) return;
        SetBusy(true);
        ErrorLabel.Text = string.Empty;
        try { await action(); }
        catch (MobileApiException ex)
        {
            _pendingSkuId = null;
            if (refreshOnConflict && ex.Code == "request_conflict")
            {
                try { Apply(await _orders.GetAsync(_orderId)); }
                catch (MobileApiException) { }
                catch (HttpRequestException) { }
                ErrorLabel.Text = $"{ex.Message} Проверьте актуальные данные SKU и дату срока перед повтором.";
            }
            else ErrorLabel.Text = ex.Message;
        }
        catch (HttpRequestException) { ErrorLabel.Text = "Ответ не подтверждён. Повторите ту же команду."; }
        catch (InvalidOperationException ex) { ErrorLabel.Text = ex.Message; }
        finally { SetBusy(false); UpdateRecovery(); await UpdateCameraAsync(); }
    }

    private async void OnRetryClicked(object? sender, EventArgs e)
    {
        if (_busy || !_process.HasPendingCommand) return;
        var action = _process.PendingLabel;
        var succeeded = false;
        SetBusy(true);
        try
        {
            Apply(await _process.RetryAsync());
            ErrorLabel.Text = string.Empty;
            succeeded = true;
        }
        catch (MobileApiException ex) { ErrorLabel.Text = ex.Message; _pendingSkuId = null; }
        catch (HttpRequestException) { ErrorLabel.Text = "Ответ не подтверждён. Повторите ту же команду."; }
        finally { SetBusy(false); UpdateRecovery(); await UpdateCameraAsync(); }
        if (!succeeded) return;
        if (action == "Добавить SKU" && _pendingSkuId is Guid skuId)
        {
            _pendingSkuId = null;
            await SelectSkuAsync(skuId, true);
        }
        else if (action is "Закрыть LPN" or "Отменить LPN")
            await Navigation.PopAsync();
        else if (action == "Открыть LPN" && _process.LastResourceId is Guid lpnId)
        {
            _lpnId = lpnId;
            await RefreshAsync();
        }
    }

    private void UpdateRecovery()
    {
        RetryButton.IsVisible = _process.HasPendingCommand;
        RetryButton.Text = $"Повторить: {_process.PendingLabel}";
        SearchButton.IsEnabled = !_busy && !_process.HasPendingCommand;
        CloseButton.IsEnabled = SearchButton.IsEnabled;
        CancelButton.IsEnabled = SearchButton.IsEnabled;
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
}

public sealed record ReceivingLpnItemRow(Guid SkuId, string Name, string Details);
public sealed record ReceivingSkuChoiceRow(Guid SkuId, string Name, string Plan, bool IsInOrder);

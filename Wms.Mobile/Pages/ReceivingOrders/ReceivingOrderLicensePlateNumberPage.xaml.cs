using Microsoft.Extensions.DependencyInjection;
using Wms.Contracts.Mobile.V1;
using Wms.Mobile.Scanning;
using Wms.Mobile.Services;

namespace Wms.Mobile;

public partial class ReceivingOrderLicensePlateNumberPage : ContentPage
{
    private ReceivingOrderReceivingProcess _process = null!;
    private readonly MobileReceivingOrderClient _orderClient;
    private readonly IOperationalBarcodeScanner _scanner;
    private readonly IServiceProvider _services;
    private Guid _orderId;
    private Guid _lpnId;
    private MobileReceivingOrderDetailsResponse? _details;
    private bool _isVisible;
    private long _scanGeneration;
    private bool _scannerSubscribed;
    private bool _busy;

    public ReceivingOrderLicensePlateNumberPage(
        MobileReceivingOrderClient orderClient,
        IOperationalBarcodeScanner scanner,
        IServiceProvider services)
    {
        InitializeComponent();
        _orderClient = orderClient;
        _scanner = scanner;
        _services = services;
        CameraScannerView.Configure(scanner);
    }

    public IReadOnlyList<ReceivingOrderLpnSkuRow> Rows { get; private set; } = [];
    public IReadOnlyList<ReceivingOrderPlannedSkuRow> PlannedSkuRows { get; private set; } = [];

    private MobileReceivingOrderLicensePlateNumberResponse LicensePlateNumber =>
        _details?.LicensePlateNumbers.SingleOrDefault(x => x.Id == _lpnId)
        ?? throw new InvalidOperationException("Палета не найдена в приходном ордере.");

    public void Show(Guid orderId, Guid lpnId, ReceivingOrderReceivingProcess process)
    {
        _process = process;
        _orderId = orderId;
        _lpnId = lpnId;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _isVisible = true;
        if (!_scannerSubscribed)
        {
            _scanner.ScanReceived += OnScanReceived;
            _scannerSubscribed = true;
        }
        await RefreshAsync(showErrors: true);
        await UpdateCameraAsync();
    }

    protected override void OnDisappearing()
    {
        _isVisible = false;
        Interlocked.Increment(ref _scanGeneration);
        CameraScannerView.Stop();
        if (_scannerSubscribed)
        {
            _scanner.ScanReceived -= OnScanReceived;
            _scannerSubscribed = false;
        }
        base.OnDisappearing();
    }

    private void OnScanReceived(object? sender, BarcodeScanEvent scanEvent)
    {
        long generation = Volatile.Read(ref _scanGeneration);
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            if (_isVisible && generation == Volatile.Read(ref _scanGeneration))
                await HandleScanAsync(scanEvent.Value);
        });
    }

    private async Task HandleScanAsync(string barcode)
    {
        if (!_isVisible || _busy || _process.HasPendingCommand || _details is null)
            return;
        if (IsLicensePlateNumberCode(barcode))
        {
            await OpenOrSwitchLicensePlateNumberAsync(barcode.Trim());
            return;
        }
        if (!LicensePlateNumber.CanEdit)
        {
            ErrorLabel.Text = "Закрытая палета доступна только для просмотра.";
            return;
        }
        await ResolveSkuAsync(barcode);
    }

    private async Task OpenOrSwitchLicensePlateNumberAsync(string code)
    {
        if (_process.HasPendingCommand)
            return;
        await ExecuteAsync(async () =>
        {
            var response = await _process.OpenLicensePlateNumberAsync(_orderId, code);
            _details = response.Details;
            if (response.SelectedLicensePlateNumberId is Guid id)
                _lpnId = id;
            ApplyDetails();
        }, "Ответ сервера не получен. Повторно отсканируйте тот же LPN.");
    }

    private async Task ResolveSkuAsync(string barcode)
    {
        SetBusy(true);
        ErrorLabel.Text = string.Empty;
        try
        {
            var candidates = await _orderClient.ResolveSkuAsync(_orderId, barcode);
            var skuIds = candidates.Select(x => x.StockKeepingUnitId).Distinct().ToArray();
            if (skuIds.Length != 1)
            {
                ErrorLabel.Text = skuIds.Length == 0
                    ? "Товар отсутствует в плане приходного ордера."
                    : "Штрихкод соответствует нескольким товарам.";
                return;
            }
            await OpenQuantityPageAsync(skuIds[0]);
        }
        catch (MobileApiException exception)
        {
            ErrorLabel.Text = exception.Message;
        }
        catch (HttpRequestException)
        {
            ErrorLabel.Text = "Не удалось определить товар. Повторите сканирование.";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void OnSkuTapped(object? sender, TappedEventArgs e)
    {
        if (!_busy && !_process.HasPendingCommand && LicensePlateNumber.CanEdit && e.Parameter is Guid skuId)
            await OpenQuantityPageAsync(skuId);
    }

    private async void OnManualSkuClicked(object? sender, EventArgs e)
    {
        if (_busy || _process.HasPendingCommand || _details is null || !LicensePlateNumber.CanEdit)
            return;

        if (ManualSkuPanel.IsVisible)
        {
            HideManualSkuSelection();
            return;
        }

        ManualSkuPanel.IsVisible = true;
        ManualSkuButton.Text = "Скрыть выбор товара";
        ManualSkuSearch.Text = string.Empty;
        UpdatePlannedSkuRows();
        await LpnScrollView.ScrollToAsync(0, 0, false);
    }

    internal void HideManualSkuSelection()
    {
        ManualSkuSearch.Unfocus();
        ManualSkuPanel.IsVisible = false;
        ManualSkuButton.Text = "Выбрать товар из ордера";
    }

    private void OnManualSkuSearchChanged(object? sender, TextChangedEventArgs e) =>
        UpdatePlannedSkuRows();

    private void UpdatePlannedSkuRows()
    {
        var search = ManualSkuSearch.Text?.Trim();
        var candidates = (_details?.Lines ?? [])
            .GroupBy(line => line.StockKeepingUnitId)
            .Select(group => group.First())
            .OrderBy(line => line.SkuName)
            .ThenBy(line => line.SkuCode)
            .Where(line => string.IsNullOrEmpty(search)
                || line.SkuName.Contains(search, StringComparison.OrdinalIgnoreCase)
                || line.SkuCode.Contains(search, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        PlannedSkuRows = candidates.Take(50)
            .Select(line => new ReceivingOrderPlannedSkuRow(
                line.StockKeepingUnitId, line.SkuName, line.SkuCode))
            .ToArray();
        ManualSkuHint.Text = candidates.Length > 50
            ? $"Показаны первые 50 из {candidates.Length}. Уточните поиск."
            : candidates.Length == 0 ? "Товар не найден в плане ордера." : string.Empty;
        OnPropertyChanged(nameof(PlannedSkuRows));
    }

    private async Task OpenQuantityPageAsync(Guid skuId)
    {
        if (_process.HasPendingCommand || _details is null || !LicensePlateNumber.CanEdit
            || !_details.Lines.Any(line => line.StockKeepingUnitId == skuId))
            return;

        ManualSkuSearch.Unfocus();
        var page = _services.GetRequiredService<ReceivingOrderLicensePlateNumberQuantityPage>();
        page.Show(_orderId, _lpnId, skuId, _process);
        await Navigation.PushAsync(page);
    }

    private async void OnCloseClicked(object? sender, EventArgs e)
    {
        if (_busy || !LicensePlateNumber.CanClose
            || _process.HasPendingCommand)
            return;
        if (!await DisplayAlertAsync(
                "Закрыть палету", "Завершить пересчёт этой палеты?", "Закрыть", "Назад"))
            return;
        await MutateAsync(
            () => _process.CloseLicensePlateNumberAsync(_orderId, _lpnId),
            "Ответ сервера не получен. Повторите закрытие этой же палеты.",
            returnToOrder: true);
    }

    private async void OnReopenClicked(object? sender, EventArgs e)
    {
        if (_busy || !LicensePlateNumber.CanReopen
            || _process.HasPendingCommand)
            return;
        await MutateAsync(
            () => _process.ReopenLicensePlateNumberAsync(_orderId, _lpnId),
            "Ответ сервера не получен. Повторите возобновление этой же палеты.");
    }

    private async void OnCancelClicked(object? sender, EventArgs e)
    {
        if (_busy || !LicensePlateNumber.CanCancel
            || _process.HasPendingCommand)
            return;
        if (!await DisplayAlertAsync(
                "Отменить палету", $"Отменить пустую палету {LicensePlateNumber.Code}?", "Отменить", "Назад"))
            return;
        await ExecuteAsync(async () =>
        {
            await _process.CancelLicensePlateNumberAsync(_orderId, _lpnId);
            await Navigation.PopAsync();
        }, "Ответ сервера не получен. Повторите отмену этой же палеты.");
    }

    private async Task MutateAsync(
        Func<Task<MobileReceivingOrderCommandResponse>> mutation,
        string uncertainMessage,
        bool returnToOrder = false)
    {
        await ExecuteAsync(async () =>
        {
            var response = await mutation();
            _details = response.Details;
            ApplyDetails();
            if (returnToOrder)
                await ReturnToMainOrderPageAsync(response.Details);
        }, uncertainMessage);
    }

    private async Task ReturnToMainOrderPageAsync(MobileReceivingOrderDetailsResponse details)
    {
        var orderPage = Navigation.NavigationStack
            .OfType<ReceivingOrderReceivingPage>()
            .LastOrDefault();
        if (orderPage is null)
        {
            await Navigation.PopAsync();
            return;
        }

        orderPage.ShowMain(details);
        var intermediatePages = Navigation.NavigationStack
            .SkipWhile(page => !ReferenceEquals(page, orderPage))
            .Skip(1)
            .Where(page => !ReferenceEquals(page, this))
            .ToArray();
        foreach (var page in intermediatePages)
            Navigation.RemovePage(page);

        await Navigation.PopAsync();
    }

    private async Task ExecuteAsync(Func<Task> action, string uncertainMessage)
    {
        SetBusy(true);
        ErrorLabel.Text = string.Empty;
        try
        {
            await action();
        }
        catch (MobileApiException exception)
        {
            ErrorLabel.Text = exception.Message;
        }
        catch (HttpRequestException)
        {
            ErrorLabel.Text = uncertainMessage;
        }
        catch (InvalidOperationException exception)
        {
            ErrorLabel.Text = exception.Message;
        }
        finally
        {
            SetBusy(false);
            await UpdateCameraAsync();
        }
    }

    private async void OnRefreshing(object? sender, EventArgs e) =>
        await RefreshAsync(showErrors: true);

    private async Task RefreshAsync(bool showErrors)
    {
        if (_busy)
        {
            LpnRefreshView.IsRefreshing = false;
            return;
        }
        SetBusy(true);
        if (showErrors) ErrorLabel.Text = string.Empty;
        try
        {
            _details = await _orderClient.GetAsync(_orderId);
            ApplyDetails();
        }
        catch (MobileApiException exception)
        {
            if (showErrors) ErrorLabel.Text = exception.Message;
        }
        catch (HttpRequestException)
        {
            if (showErrors) ErrorLabel.Text = "Сервер WMS недоступен.";
        }
        finally
        {
            LpnRefreshView.IsRefreshing = false;
            SetBusy(false);
        }
    }

    private void ApplyDetails()
    {
        var lpn = LicensePlateNumber;
        if (!lpn.CanEdit && ManualSkuPanel.IsVisible)
            HideManualSkuSelection();
        ManualSkuButton.IsVisible = lpn.CanEdit;
        if (ManualSkuPanel.IsVisible)
            UpdatePlannedSkuRows();
        Title = lpn.Code;
        StatusLabel.Text = $"{lpn.StatusName} · {lpn.ResponsibleUserName}";
        InstructionLabel.Text = lpn.CanEdit
            ? "Выберите или отсканируйте товар. LPN переключается сканированием."
            : "Палета доступна только для просмотра.";
        CancelButton.IsVisible = lpn.CanCancel;
        CloseButton.IsVisible = lpn.CanClose;
        ReopenButton.IsVisible = lpn.CanReopen;
        Rows = lpn.Items
            .Select(item =>
            {
                var plan = _details!.Lines
                    .Where(x => x.StockKeepingUnitId == item.StockKeepingUnitId)
                    .Sum(x => x.PlanQuantity);
                var unit = string.IsNullOrWhiteSpace(item.UnitOfMeasure) ? string.Empty : $" {item.UnitOfMeasure}";
                return new ReceivingOrderLpnSkuRow(
                    item.StockKeepingUnitId,
                    item.SkuCode,
                    item.SkuName,
                    $"План {plan:g}{unit}",
                    $"В палете {item.Quantity:g}{unit}");
            })
            .ToArray();
        OnPropertyChanged(nameof(Rows));
    }


    private async void OnRetryCommandClicked(object? sender, EventArgs e)
    {
        if (_busy || !_process.HasPendingCommand)
            return;
        bool closing = _process.IsClosePending;
        bool cancelling = _process.IsCancelLpnPending;
        await ExecuteAsync(async () =>
        {
            var response = await _process.RetryAsync();
            _details = response.Details;
            if (response.SelectedLicensePlateNumberId is Guid id)
                _lpnId = id;
            ApplyDetails();
            if (closing)
                await ReturnToMainOrderPageAsync(response.Details);
            else if (cancelling)
                await Navigation.PopAsync();
        }, "Ответ не получен. Повторите сохранённую команду.");
    }

    private void UpdateRecoveryControls()
    {
        RetryCommandButton.IsVisible = _process.HasPendingCommand;
        RetryCommandButton.IsEnabled = !_busy;
        RetryCommandButton.Text = $"Повторить: {_process.PendingLabel}";
        NavigationPage.SetHasBackButton(this, !_busy && !_process.HasPendingCommand);
    }

    protected override bool OnBackButtonPressed()
    {
        if (_busy || _process.HasPendingCommand)
        {
            ErrorLabel.Text = "Сначала получите результат текущей команды.";
            return true;
        }
        return base.OnBackButtonPressed();
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        ProgressIndicator.Opacity = busy ? 1 : 0;
        CloseButton.IsEnabled = !busy && !_process.HasPendingCommand;
        ReopenButton.IsEnabled = !busy && !_process.HasPendingCommand;
        CancelButton.IsEnabled = !busy && !_process.HasPendingCommand;
        ManualSkuButton.IsEnabled = !busy && !_process.HasPendingCommand;
        UpdateRecoveryControls();
    }

    private void OnNonScanControlLoaded(object? sender, EventArgs e)
    {
        if (sender is VisualElement element)
            AndroidFocus.Suppress(element);
    }

    private async Task UpdateCameraAsync()
    {
        if (_isVisible && !_busy && !_process.HasPendingCommand && _scanner.ActiveSource == BarcodeScanSource.Camera)
            await CameraScannerView.StartAsync();
        else
            CameraScannerView.Stop();
    }

    private static bool IsLicensePlateNumberCode(string value)
    {
        var code = value.Trim();
        return code.Length == 15
            && code.StartsWith("LPN", StringComparison.OrdinalIgnoreCase)
            && code[3..].All(char.IsDigit);
    }
}

public sealed record ReceivingOrderLpnSkuRow(
    Guid StockKeepingUnitId,
    string SkuCode,
    string SkuName,
    string PlanText,
    string QuantityText);

public sealed record ReceivingOrderPlannedSkuRow(
    Guid StockKeepingUnitId,
    string SkuName,
    string SkuCode);

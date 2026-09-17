using Microsoft.Extensions.DependencyInjection;
using Wms.Contracts.Mobile.V1;
using Wms.Mobile.Scanning;
using Wms.Mobile.Services;

namespace Wms.Mobile;

public partial class ReceivingOrderLicensePlateNumberPage : ContentPage
{
    private readonly ReceivingOrderReceivingProcess _process;
    private readonly MobileReceivingOrderClient _orderClient;
    private readonly IOperationalBarcodeScanner _scanner;
    private readonly IServiceProvider _services;
    private Guid _orderId;
    private Guid _lpnId;
    private MobileReceivingOrderDetailsResponse? _details;
    private bool _isVisible;
    private bool _scannerSubscribed;
    private bool _busy;

    public ReceivingOrderLicensePlateNumberPage(
        ReceivingOrderReceivingProcess process,
        MobileReceivingOrderClient orderClient,
        IOperationalBarcodeScanner scanner,
        IServiceProvider services)
    {
        InitializeComponent();
        _process = process;
        _orderClient = orderClient;
        _scanner = scanner;
        _services = services;
        CameraScannerView.Configure(scanner);
    }

    public IReadOnlyList<ReceivingOrderLpnSkuRow> Rows { get; private set; } = [];

    private MobileReceivingOrderLicensePlateNumberResponse LicensePlateNumber =>
        _details?.LicensePlateNumbers.SingleOrDefault(x => x.Id == _lpnId)
        ?? throw new InvalidOperationException("Паллета не найдена в приходном ордере.");

    public void Show(Guid orderId, Guid lpnId)
    {
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
        CameraScannerView.Stop();
        if (_scannerSubscribed)
        {
            _scanner.ScanReceived -= OnScanReceived;
            _scannerSubscribed = false;
        }
        base.OnDisappearing();
    }

    private void OnScanReceived(object? sender, BarcodeScanEvent scanEvent) =>
        MainThread.BeginInvokeOnMainThread(async () => await HandleScanAsync(scanEvent.Value));

    private async Task HandleScanAsync(string barcode)
    {
        if (_busy || _details is null)
            return;
        if (IsLicensePlateNumberCode(barcode))
        {
            await OpenOrSwitchLicensePlateNumberAsync(barcode.Trim());
            return;
        }
        if (!LicensePlateNumber.CanEdit)
        {
            ErrorLabel.Text = "Закрытая паллета доступна только для просмотра.";
            return;
        }
        await ResolveSkuAsync(barcode);
    }

    private async Task OpenOrSwitchLicensePlateNumberAsync(string code)
    {
        if (_process.HasPendingCommand && !_process.IsOpenLpnPending)
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
        if (!_busy && LicensePlateNumber.CanEdit && e.Parameter is Guid skuId)
            await OpenQuantityPageAsync(skuId);
    }

    private async Task OpenQuantityPageAsync(Guid skuId)
    {
        var page = _services.GetRequiredService<ReceivingOrderLicensePlateNumberQuantityPage>();
        page.Show(_orderId, _lpnId, skuId);
        await Navigation.PushAsync(page);
    }

    private async void OnCloseClicked(object? sender, EventArgs e)
    {
        if (_busy || !LicensePlateNumber.CanClose
            || (_process.HasPendingCommand && !_process.IsLpnMutationPending))
            return;
        if (!_process.IsLpnMutationPending && !await DisplayAlertAsync(
                "Закрыть паллету", "Завершить пересчёт этой паллеты?", "Закрыть", "Назад"))
            return;
        await MutateAsync(
            () => _process.CloseLicensePlateNumberAsync(_orderId, _lpnId),
            "Ответ сервера не получен. Повторите закрытие этой же паллеты.");
    }

    private async void OnReopenClicked(object? sender, EventArgs e)
    {
        if (_busy || !LicensePlateNumber.CanReopen
            || (_process.HasPendingCommand && !_process.IsLpnMutationPending))
            return;
        await MutateAsync(
            () => _process.ReopenLicensePlateNumberAsync(_orderId, _lpnId),
            "Ответ сервера не получен. Повторите возобновление этой же паллеты.");
    }

    private async void OnCancelClicked(object? sender, EventArgs e)
    {
        if (_busy || !LicensePlateNumber.CanCancel
            || (_process.HasPendingCommand && !_process.IsCancelLpnPending))
            return;
        if (!_process.IsCancelLpnPending && !await DisplayAlertAsync(
                "Отменить паллету", $"Отменить пустую паллету {LicensePlateNumber.Code}?", "Отменить", "Назад"))
            return;
        await ExecuteAsync(async () =>
        {
            await _process.CancelLicensePlateNumberAsync(_orderId, _lpnId);
            await Navigation.PopAsync();
        }, "Ответ сервера не получен. Повторите отмену этой же паллеты.");
    }

    private async Task MutateAsync(
        Func<Task<MobileReceivingOrderCommandResponse>> mutation,
        string uncertainMessage)
    {
        await ExecuteAsync(async () =>
        {
            var response = await mutation();
            _details = response.Details;
            ApplyDetails();
        }, uncertainMessage);
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
        CodeLabel.Text = lpn.Code;
        StatusLabel.Text = $"{lpn.StatusName} · {lpn.ResponsibleUserName}";
        InstructionLabel.Text = lpn.CanEdit
            ? "Выберите или отсканируйте товар. LPN переключается сканированием."
            : "Паллета доступна только для просмотра.";
        CancelButton.IsVisible = lpn.CanCancel;
        CloseButton.IsVisible = lpn.CanClose;
        ReopenButton.IsVisible = lpn.CanReopen;
        Rows = _details!.Lines
            .GroupBy(x => new { x.StockKeepingUnitId, x.SkuCode, x.SkuName, x.UnitOfMeasure })
            .Select(group =>
            {
                var item = lpn.Items.SingleOrDefault(x => x.StockKeepingUnitId == group.Key.StockKeepingUnitId);
                var unit = string.IsNullOrWhiteSpace(group.Key.UnitOfMeasure) ? string.Empty : $" {group.Key.UnitOfMeasure}";
                return new ReceivingOrderLpnSkuRow(
                    group.Key.StockKeepingUnitId,
                    group.Key.SkuCode,
                    group.Key.SkuName,
                    $"План {group.Sum(x => x.PlanQuantity):g}{unit}",
                    item is null ? $"В паллете —{unit}" : $"В паллете {item.Quantity:g}{unit}");
            })
            .ToArray();
        OnPropertyChanged(nameof(Rows));
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        ProgressIndicator.Opacity = busy ? 1 : 0;
        CloseButton.IsEnabled = !busy;
        ReopenButton.IsEnabled = !busy;
        CancelButton.IsEnabled = !busy;
    }

    private async Task UpdateCameraAsync()
    {
        if (_isVisible && _scanner.ActiveSource == BarcodeScanSource.Camera)
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

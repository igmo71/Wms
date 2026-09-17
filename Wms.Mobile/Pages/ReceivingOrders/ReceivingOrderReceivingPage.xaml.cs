using Microsoft.Extensions.DependencyInjection;
using Wms.Contracts.Mobile.V1;
using Wms.Mobile.Scanning;
using Wms.Mobile.Services;

namespace Wms.Mobile;

public partial class ReceivingOrderReceivingPage : ContentPage
{
    private readonly ReceivingOrderReceivingProcess _process;
    private readonly MobileReceivingOrderClient _orderClient;
    private readonly MobileReferenceDataClient _referenceDataClient;
    private readonly IOperationalBarcodeScanner _scanner;
    private readonly IServiceProvider _services;
    private MobileReceivingOrderDetailsResponse? _details;
    private MobileOrderSynchronizationResponse? _synchronization;
    private bool _isVisible;
    private bool _scannerSubscribed;
    private bool _busy;
    private bool _awaitingReceivingLocation;
    private bool _lpnTabActive;
    private bool _hasAppeared;

    public ReceivingOrderReceivingPage(
        ReceivingOrderReceivingProcess process,
        MobileReceivingOrderClient orderClient,
        MobileReferenceDataClient referenceDataClient,
        IOperationalBarcodeScanner scanner,
        IServiceProvider services)
    {
        InitializeComponent();
        _process = process;
        _orderClient = orderClient;
        _referenceDataClient = referenceDataClient;
        _scanner = scanner;
        _services = services;
        CameraScannerView.Configure(scanner);
    }

    public IReadOnlyList<ReceivingOrderLineRow> LineRows { get; private set; } = [];
    public IReadOnlyList<ReceivingOrderLpnRow> LicensePlateNumbers { get; private set; } = [];

    private MobileReceivingOrderDetailsResponse Details =>
        _details ?? throw new InvalidOperationException("Приходный ордер не загружен.");

    private bool CanUseLicensePlateNumbers =>
        _details?.Order is { Status: MobileReceivingOrderStatus.InReceiving, IsParticipant: true };

    public void Show(MobileReceivingOrderDetailsResponse details)
    {
        _synchronization = null;
        ApplyDetails(details);
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

        if (_hasAppeared)
            await RefreshAsync(showErrors: false);
        _hasAppeared = true;
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
        if (_busy)
            return;
        if (_awaitingReceivingLocation)
        {
            await ResolveReceivingLocationAsync(barcode);
            return;
        }
        if (!CanUseLicensePlateNumbers)
            return;
        if (!IsLicensePlateNumberCode(barcode))
        {
            ErrorLabel.Text = "Товар сканируется на экране выбранной палеты.";
            return;
        }
        await OpenLicensePlateNumberAsync(barcode.Trim());
    }

    private async void OnStartReceivingClicked(object? sender, EventArgs e)
    {
        if (_busy || _process.HasPendingCommand || _details is null)
            return;
        _awaitingReceivingLocation = true;
        ErrorLabel.Text = string.Empty;
        InstructionLabel.Text = "Отсканируйте позицию зоны приёмки.";
        StartReceivingButton.IsVisible = false;
        await UpdateCameraAsync();
    }

    private async Task ResolveReceivingLocationAsync(string barcode)
    {
        SetBusy(true);
        ErrorLabel.Text = string.Empty;
        try
        {
            var location = await _referenceDataClient.ResolveStorageLocationAsync(
                barcode, Details.Order.WarehouseId, MobileStorageLocationContext.Receiving);
            var confirmed = await DisplayAlertAsync(
                "Начать приёмку", $"Позиция {location.Address}?", "Начать", "Назад");
            if (!confirmed)
            {
                _awaitingReceivingLocation = false;
                ApplyDetails(Details);
                return;
            }

            var response = await _process.StartAsync(Details.Order.Id, barcode);
            _awaitingReceivingLocation = false;
            ApplyDetails(response.Details);
        }
        catch (MobileApiException exception)
        {
            ErrorLabel.Text = exception.Message;
        }
        catch (HttpRequestException)
        {
            ErrorLabel.Text = "Сервер WMS недоступен. Повторите сканирование позиции.";
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

    private async Task OpenLicensePlateNumberAsync(string code)
    {
        if (_process.HasPendingCommand && !_process.IsOpenLpnPending)
            return;
        SetBusy(true);
        ErrorLabel.Text = string.Empty;
        try
        {
            var response = await _process.OpenLicensePlateNumberAsync(Details.Order.Id, code);
            ApplyDetails(response.Details);
            if (response.SelectedLicensePlateNumberId is Guid id)
                await OpenLicensePlateNumberPageAsync(id);
        }
        catch (MobileApiException exception)
        {
            ErrorLabel.Text = exception.Message;
        }
        catch (HttpRequestException)
        {
            ErrorLabel.Text = "Ответ сервера не получен. Повторно отсканируйте тот же LPN.";
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

    private async void OnLicensePlateNumberTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is Guid id && !_busy)
            await OpenLicensePlateNumberPageAsync(id);
    }

    private async Task OpenLicensePlateNumberPageAsync(Guid id)
    {
        var page = _services.GetRequiredService<ReceivingOrderLicensePlateNumberPage>();
        page.Show(Details.Order.Id, id);
        await Navigation.PushAsync(page);
    }

    private async void OnSkuTapped(object? sender, TappedEventArgs e)
    {
        if (_busy || e.Parameter is not Guid skuId)
            return;
        var page = _services.GetRequiredService<ReceivingOrderSkuDistributionPage>();
        page.Show(Details.Order.Id, skuId);
        await Navigation.PushAsync(page);
    }

    private async void OnCancelLicensePlateNumberClicked(object? sender, EventArgs e)
    {
        if (_busy || sender is not Button { CommandParameter: Guid id })
            return;
        var lpn = Details.LicensePlateNumbers.SingleOrDefault(x => x.Id == id);
        if (lpn?.CanCancel != true || (_process.HasPendingCommand && !_process.IsCancelLpnPending))
            return;
        if (!_process.IsCancelLpnPending && !await DisplayAlertAsync(
                "Отменить палету", $"Отменить пустую палету {lpn.Code}?", "Отменить", "Назад"))
            return;

        SetBusy(true);
        ErrorLabel.Text = string.Empty;
        try
        {
            var response = await _process.CancelLicensePlateNumberAsync(Details.Order.Id, id);
            _synchronization = null;
            ApplyDetails(response.Details);
        }
        catch (MobileApiException exception)
        {
            ErrorLabel.Text = exception.Message;
        }
        catch (HttpRequestException)
        {
            ErrorLabel.Text = "Ответ сервера не получен. Повторите отмену этой же палеты.";
        }
        catch (InvalidOperationException exception)
        {
            ErrorLabel.Text = exception.Message;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void OnCompleteReceivingClicked(object? sender, EventArgs e)
    {
        if (_busy || _synchronization?.Fingerprint is not { Length: > 0 } fingerprint
            || (_process.HasPendingCommand && !_process.IsCompletionPending))
            return;
        if (!_process.IsCompletionPending && !await DisplayAlertAsync(
                "Завершить приёмку", "Завершить ордер и оприходовать принятый товар?", "Завершить", "Назад"))
            return;

        SetBusy(true);
        ErrorLabel.Text = string.Empty;
        var refreshAfterFailure = false;
        try
        {
            var response = await _process.CompleteAsync(Details.Order.Id, fingerprint);
            ApplyDetails(response.Details);
            await DisplayAlertAsync("Приёмка завершена", "Ордер успешно завершён.", "Готово");
            await Navigation.PopAsync();
        }
        catch (MobileApiException exception)
        {
            ErrorLabel.Text = exception.Message;
            refreshAfterFailure = true;
        }
        catch (HttpRequestException)
        {
            ErrorLabel.Text = "Ответ сервера не получен. Повторите завершение приёмки.";
        }
        catch (InvalidOperationException exception)
        {
            ErrorLabel.Text = exception.Message;
        }
        finally
        {
            SetBusy(false);
        }
        if (refreshAfterFailure)
            await RefreshAsync(showErrors: false);
    }

    private void OnProductsTabClicked(object? sender, EventArgs e) => SetLpnTab(false);
    private void OnLpnTabClicked(object? sender, EventArgs e) => SetLpnTab(true);

    private void SetLpnTab(bool active)
    {
        if (active && LicensePlateNumbers.Count == 0)
            return;
        _lpnTabActive = active;
        ProductLinesPanel.IsVisible = !active;
        LpnPanel.IsVisible = active;
        ProductsTabButton.Opacity = active ? 0.65 : 1;
        LpnTabButton.Opacity = active ? 1 : 0.65;
    }

    private async void OnOrderRefreshing(object? sender, EventArgs e) =>
        await RefreshAsync(showErrors: true);

    private async Task RefreshAsync(bool showErrors)
    {
        if (_busy || _details is null)
        {
            OrderRefreshView.IsRefreshing = false;
            return;
        }
        SetBusy(true);
        if (showErrors)
            ErrorLabel.Text = string.Empty;
        try
        {
            ApplyDetails(await _orderClient.GetAsync(Details.Order.Id));
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
            OrderRefreshView.IsRefreshing = false;
            SetBusy(false);
        }
    }

    private void ApplyDetails(MobileReceivingOrderDetailsResponse details)
    {
        _details = details;
        _synchronization = OrderSynchronizationPresentation.MergeOpeningAssessment(
            _synchronization, details.Order.Synchronization);
        NumberLabel.Text = details.Order.Number;
        StatusLabel.Text = details.Order.Status switch
        {
            MobileReceivingOrderStatus.ReadyForReceiving => "Готов",
            MobileReceivingOrderStatus.InReceiving => "В работе",
            MobileReceivingOrderStatus.ProcessingRequired => "Обработка",
            _ => "Завершён"
        };
        FactLabel.Text = $"Факт: {details.Order.Progress.FactQuantity:g} из {details.Order.Progress.PlanQuantity:g}";
        CheckedLabel.Text = $"Проверено: {details.Order.Progress.ConfirmedLineCount} из {details.Order.Progress.TotalLineCount}";
        SynchronizationPanel.IsVisible = OrderSynchronizationPresentation.HasIssue(_synchronization);
        SynchronizationDetailsLabel.Text = OrderSynchronizationPresentation.BuildDetails(_synchronization);
        LineRows = details.Lines.Select(ReceivingOrderLineRow.From).ToArray();
        LicensePlateNumbers = details.LicensePlateNumbers
            .Where(x => x.Status != MobileLicensePlateNumberStatus.Cancelled)
            .Select(x => new ReceivingOrderLpnRow(
                x.Id, x.Code, x.StatusName, x.ResponsibleUserName, x.CanCancel))
            .ToArray();
        OnPropertyChanged(nameof(LineRows));
        OnPropertyChanged(nameof(LicensePlateNumbers));
        LpnTabButton.IsEnabled = LicensePlateNumbers.Count > 0;
        if (_lpnTabActive && LicensePlateNumbers.Count == 0)
            _lpnTabActive = false;
        SetLpnTab(_lpnTabActive);
        StartReceivingButton.IsVisible = details.Order.Status == MobileReceivingOrderStatus.ReadyForReceiving
            && !_awaitingReceivingLocation;
        StartReceivingButton.IsEnabled = !_busy
            && !_process.HasPendingCommand
            && OrderSynchronizationPresentation.CanPerformCriticalTransition(_synchronization);
        CompleteReceivingButton.IsVisible = details.Order.Status == MobileReceivingOrderStatus.InReceiving
            && details.Order.IsParticipant;
        CompleteReceivingButton.IsEnabled = CanCompleteReceiving();
        InstructionLabel.Text = CompleteReceivingButton.IsVisible
            ? GetCompletionHint()
            : CanUseLicensePlateNumbers
                ? "Сканируйте LPN на любой вкладке. Товар — внутри палеты."
            : details.Order.Status == MobileReceivingOrderStatus.ReadyForReceiving
                ? "Начните приёмку."
                : string.Empty;
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        CommandProgressIndicator.Opacity = busy ? 1 : 0;
        StartReceivingButton.IsEnabled = !busy
            && !_process.HasPendingCommand
            && _synchronization is not null
            && OrderSynchronizationPresentation.CanPerformCriticalTransition(_synchronization);
        CompleteReceivingButton.IsEnabled = CanCompleteReceiving();
    }

    private bool CanCompleteReceiving()
    {
        if (_busy
            || _details is not
            {
                Order: { Status: MobileReceivingOrderStatus.InReceiving, IsParticipant: true },
                LicensePlateNumbers: var licensePlateNumbers,
                Lines: var lines
            }
            || _synchronization is not { Fingerprint.Length: > 0 } synchronization)
        {
            return false;
        }

        return (_process.IsCompletionPending || !_process.HasPendingCommand)
            && licensePlateNumbers.All(x => x.Status != MobileLicensePlateNumberStatus.Open)
            && lines.All(x => x.FactQuantity.HasValue)
            && !synchronization.RequiresManager
            && OrderSynchronizationPresentation.CanPerformCriticalTransition(synchronization);
    }

    private string GetCompletionHint()
    {
        if (_details is null || _synchronization is null)
            return "Проверяем возможность завершения приёмки…";
        if (_details.LicensePlateNumbers.Any(x => x.Status == MobileLicensePlateNumberStatus.Open))
            return "Для завершения закройте все палеты.";
        if (_details.Lines.Any(x => !x.FactQuantity.HasValue))
            return "Для завершения пересчитайте всю номенклатуру.";
        if (_synchronization.RequiresManager)
            return "Есть расхождение количества — требуется решение Manager в Web.";
        if (!OrderSynchronizationPresentation.CanPerformCriticalTransition(_synchronization))
            return "Завершение недоступно: проверьте сообщение сверки с 1С.";
        return "Все палеты закрыты, план и факт совпадают.";
    }

    private async Task UpdateCameraAsync()
    {
        if (_isVisible && _scanner.ActiveSource == BarcodeScanSource.Camera
            && (_awaitingReceivingLocation || CanUseLicensePlateNumbers))
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

public sealed record ReceivingOrderLpnRow(
    Guid Id,
    string Code,
    string StatusName,
    string ResponsibleUserName,
    bool CanCancel);

public sealed record ReceivingOrderLineRow(
    int LineNumber,
    Guid StockKeepingUnitId,
    string SkuCode,
    string SkuName,
    string PlanText,
    string FactText,
    Color BackgroundColor,
    Color FactColor)
{
    public static ReceivingOrderLineRow From(MobileReceivingOrderLineResponse line)
    {
        var unit = string.IsNullOrWhiteSpace(line.UnitOfMeasure) ? string.Empty : $" {line.UnitOfMeasure}";
        var background = !line.FactQuantity.HasValue
            ? Colors.White
            : line.FactQuantity.Value == line.PlanQuantity
                ? Color.FromArgb("#E8F5E9")
                : Color.FromArgb("#FFF4CC");
        return new ReceivingOrderLineRow(
            line.LineNumber,
            line.StockKeepingUnitId,
            line.SkuCode,
            line.SkuName,
            $"План {line.PlanQuantity:g}{unit}",
            line.FactQuantity.HasValue ? $"Факт {line.FactQuantity:g}{unit}" : "?",
            background,
            line.FactQuantity.HasValue ? Colors.Black : Colors.DarkRed);
    }
}

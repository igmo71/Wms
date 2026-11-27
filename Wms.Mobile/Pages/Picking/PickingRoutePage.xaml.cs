using System.Globalization;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Wms.Contracts.Mobile.V1;
using Wms.Mobile.Scanning;
using Wms.Mobile.Services;

namespace Wms.Mobile;

public partial class PickingRoutePage : ContentPage
{
    private readonly MobilePickingClient _client;
    private readonly MobileReplenishmentClient _replenishments;
    private readonly IOperationalBarcodeScanner _scanner;
    private readonly MobileWarehouseSelection _selection;
    private readonly IServiceProvider _services;
    private PickingAssemblyProcess _process;
    private MobilePickingAssemblyResponse? _assembly;
    private MobilePickingReplenishmentsResponse? _replenishmentList;
    private bool _orderingReplenishment;
    private bool _busy;
    private bool _visible;
    private long _scanGeneration;

    public PickingRoutePage(MobilePickingClient client, MobileReplenishmentClient replenishments,
        IOperationalBarcodeScanner scanner,
        MobileWarehouseSelection selection,
        PickingAssemblyProcess process,
        IServiceProvider services)
    {
        InitializeComponent();
        _client = client;
        _replenishments = replenishments;
        _scanner = scanner;
        _selection = selection;
        _process = process;
        _services = services;
        BindingContext = this;
        CameraScannerView.Configure(scanner);
    }

    public IReadOnlyList<PickingRouteRow> Rows { get; private set; } = [];
    private bool CanWork => _assembly is { Status: MobilePickingAssemblyStatus.InProgress } assembly
        && assembly.ResponsibleUserId == _selection.CurrentUserId;

    public void Show(MobilePickingAssemblyResponse assembly, PickingAssemblyProcess process)
    {
        _process = process;
        ApplyAssembly(assembly);
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
        RouteRefreshView.IsRefreshing = false;
    }

    private async Task RefreshAsync(MobilePickingAssemblyResponse? updatedAssembly = null)
    {
        if (_busy || _assembly is null) return;
        using var measurement = MobilePerformanceDiagnostics.Load("Маршрут");
        SetBusy(true);
        ErrorLabel.Text = string.Empty;
        try
        {
            var assembly = updatedAssembly ?? await _client.GetAsync(_assembly.Id, _assembly.WarehouseId);
            ApplyAssembly(assembly);
            var routeTask = LoadRouteAsync(assembly);
            var replenishmentTask = LoadReplenishmentsAsync(assembly);
            await Task.WhenAll(routeTask, replenishmentTask);
            var route = await routeTask;
            if (route is null)
            {
                ClearRoute();
                if (_assembly is { Status: MobilePickingAssemblyStatus.InProgress })
                {
                    EmptyLabel.Text = "Сборка назначена другому оператору.";
                    EmptyLabel.IsVisible = true;
                }
                return;
            }
            var (replenishmentList, replenishmentError) = await replenishmentTask;
            _replenishmentList = replenishmentList?.Revision == route.Revision ? replenishmentList : null;
            if (replenishmentList is not null && _replenishmentList is null)
                replenishmentError = "Сборка изменилась. Потяните вниз, чтобы обновить подпитки.";
            ErrorLabel.Text = replenishmentError ?? string.Empty;
            ApplyRoute(route);
        }
        catch (MobileApiException exception)
        {
            ClearRoute();
            ErrorLabel.Text = exception.Message;
        }
        catch (HttpRequestException)
        {
            ClearRoute();
            ErrorLabel.Text = "Не удалось обновить маршрут.";
        }
        finally { SetBusy(false); await UpdateCameraAsync(); }
    }

    private void ApplyRoute(MobilePickingRouteResponse route)
    {
        var skus = route.Work.Skus.ToDictionary(x => x.SkuId);
        var reservedBySku = route.Steps.GroupBy(x => x.SkuId)
            .ToDictionary(x => x.Key, x => x.Sum(step => step.Quantity));
        var uncoveredBySku = route.Gaps.GroupBy(x => x.SkuId)
            .ToDictionary(x => x.Key, x => x.Sum(gap => gap.Quantity));
        var shownSkus = new HashSet<Guid>();
        var rows = route.Steps.Select((step, index) =>
            CreateRow(step.SkuId, step, index == 0, shownSkus.Add(step.SkuId))).ToList();
        foreach (var sku in route.Work.Skus.Where(x => uncoveredBySku.ContainsKey(x.SkuId) && !shownSkus.Contains(x.SkuId)))
            rows.Add(CreateRow(sku.SkuId, null, false, true));

        Rows = rows;
        OnPropertyChanged(nameof(Rows));
        RouteHint.IsVisible = route.Steps.Count > 0;
        EmptyLabel.Text = route.Work.Skus.All(x => x.ConfirmedQuantity >= x.PlannedQuantity)
            ? "Отбор по маршруту завершён." : "Шагов маршрута нет.";
        EmptyLabel.IsVisible = Rows.Count == 0;

        PickingRouteRow CreateRow(Guid skuId, MobilePickingRouteStepResponse? step, bool isCurrent, bool showSummary)
        {
            var sku = skus.GetValueOrDefault(skuId);
            var plan = sku?.PlannedQuantity.ToString("0.###", CultureInfo.CurrentCulture) ?? "—";
            var fact = sku?.ConfirmedQuantity.ToString("0.###", CultureInfo.CurrentCulture) ?? "—";
            var remaining = sku is null ? 0 : sku.PlannedQuantity - sku.ConfirmedQuantity;
            var locationName = step?.LocationName;
            var expiry = step?.ExpirationDate is DateOnly date ? $"до {date:dd.MM.yyyy}" : string.Empty;
            var pallet = step?.DirectPalletPick == true;
            var detail = pallet ? string.Join(" · ", new[] { expiry, "снять весь поддон" }.Where(x => x.Length > 0)) : expiry;
            return new(skuId, step?.LocationCode, pallet, isCurrent, sku?.Name ?? sku?.Code ?? skuId.ToString(),
                $"План {plan} {sku?.Unit ?? "—"}", $"Резерв {reservedBySku.GetValueOrDefault(skuId):0.###}",
                $"Факт {fact}", step is null ? string.Empty : $"Взять {step.Quantity:0.###}", detail)
            {
                Address = string.IsNullOrWhiteSpace(locationName) ? step?.LocationCode : locationName,
                HasExpiredStock = step?.CellAvailability?.HasExpiredStock == true,
                StockSummary = new(showSummary ? uncoveredBySku.GetValueOrDefault(skuId) : 0,
                    _replenishmentList?.StockBySku.GetValueOrDefault(skuId)?.FreeStorageQuantity),
                Replenishment = showSummary ? PickingReplenishmentAction.ForSku(_replenishmentList, skuId, remaining)
                    : PickingReplenishmentAction.Hidden
            };
        }
    }

    private async Task<(MobilePickingReplenishmentsResponse? List, string? Error)> LoadReplenishmentsAsync(
        MobilePickingAssemblyResponse assembly)
    {
        if (!CanWork) return (null, null);
        try { return (await _replenishments.AssemblyAsync(assembly.Id, assembly.WarehouseId), null); }
        catch (MobileApiException exception) { return (null, exception.Message); }
        catch (HttpRequestException) { return (null, "Не удалось обновить подпитки."); }
    }

    private async Task<MobilePickingRouteResponse?> LoadRouteAsync(MobilePickingAssemblyResponse assembly)
    {
        if (!CanWork)
            return null;
        try
        {
            return await _client.RouteAsync(assembly.Id, assembly.WarehouseId);
        }
        catch (MobileApiException exception) when (exception.StatusCode == HttpStatusCode.Conflict)
        {
            // The assembly can close or change hands between the two reads.
            ApplyAssembly(await _client.GetAsync(assembly.Id, assembly.WarehouseId));
            if (CanWork)
                throw;
            return null;
        }
    }

    private void ClearRoute()
    {
        Rows = [];
        _replenishmentList = null;
        OnPropertyChanged(nameof(Rows));
        RouteHint.IsVisible = false;
        EmptyLabel.IsVisible = false;
    }

    private void ApplyAssembly(MobilePickingAssemblyResponse assembly)
    {
        _assembly = assembly;
        PickingTitle.Set(HeaderTitleLabel, "Отбор", assembly.Number);
        Title = $"Отбор · {assembly.Number}";
        StatusLabel.Text = assembly.Status switch
        {
            MobilePickingAssemblyStatus.Draft => "Черновик",
            MobilePickingAssemblyStatus.Preparing => "Запрос в 1С КОтбору",
            MobilePickingAssemblyStatus.InProgress => "В работе",
            MobilePickingAssemblyStatus.Closed => "Сборка закрыта",
            MobilePickingAssemblyStatus.CancellationPending => "Отменяется",
            MobilePickingAssemblyStatus.Cancelled => "Отменена",
            _ => "Неизвестный статус"
        };
        OwnerLabel.Text = assembly.ResponsibleDisplayName;
    }

    private async void OnStepTapped(object? sender, TappedEventArgs e)
    {
        if (_busy || _process.HasPendingCommand || _assembly is null
            || e.Parameter is not PickingRouteRow row) return;
        await OpenStepAsync(row, null);
    }

    private async void OnChooseCellClicked(object? sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: PickingRouteRow { CanChooseCell: true } row })
            await OpenStepAsync(row, null);
    }

    private void OnScanReceived(object? sender, BarcodeScanEvent scan)
    {
        var generation = Volatile.Read(ref _scanGeneration);
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            if (_visible && generation == Volatile.Read(ref _scanGeneration))
                await OpenScannedStepAsync(scan.Value);
        });
    }

    private async Task OpenScannedStepAsync(string? code)
    {
        if (!_visible || _busy || _process.HasPendingCommand || !CanWork || !RouteHint.IsVisible
            || string.IsNullOrWhiteSpace(code)) return;
        var scannedCode = code.Trim();
        var row = Rows.FirstOrDefault(x => string.Equals(x.LocationCode,
            scannedCode, StringComparison.OrdinalIgnoreCase));
        if (row is null)
        {
            ErrorLabel.Text = "Ячейки нет в текущем маршруте. Обновите маршрут или выберите SKU.";
            await UpdateCameraAsync();
            return;
        }
        await OpenStepAsync(row, scannedCode);
    }

    private async Task OpenStepAsync(PickingRouteRow row, string? scannedCode)
    {
        if (_assembly is null || !CanWork || _busy || _process.HasPendingCommand) return;
        SetBusy(true);
        ErrorLabel.Text = string.Empty;
        try
        {
            var page = _services.GetRequiredService<PickingEntryPage>();
            page.ShowPick(_assembly, _process, row.SkuId,
                row.LocationCode, row.DirectPalletPick,
                row.HasStep ? PickingEntryOrigin.Route : PickingEntryOrigin.Sku, scannedCode);
            await Navigation.PushAsync(page);
        }
        finally { SetBusy(false); await UpdateCameraAsync(); }
    }

    private async void OnReplenishmentClicked(object? sender, EventArgs e)
    {
        if (_busy || _process.HasPendingCommand || !CanWork || _assembly is not { } assembly
            || _replenishmentList is not { } list
            || sender is not Button { CommandParameter: PickingRouteRow row } || !row.Replenishment.IsVisible) return;
        SetBusy(true);
        try
        {
            if (!await row.Replenishment.ConfirmOrderAsync(this)) return;
            _orderingReplenishment = true;
            await RunWorkAsync(() => _process.OrderReplenishmentAsync(assembly.Id, assembly.WarehouseId,
                list.Revision, row.SkuId));
        }
        finally { SetBusy(false); await UpdateCameraAsync(); }
    }

    private async void OnSkuClicked(object? sender, EventArgs e) =>
        await SwitchSectionAsync(PickingSection.Sku);

    private async void OnSourcesClicked(object? sender, EventArgs e) =>
        await SwitchSectionAsync(PickingSection.Sources);

    private async Task SwitchSectionAsync(PickingSection section)
    {
        if (_busy || _process.HasPendingCommand || _assembly is null) return;
        SetBusy(true);
        try
        {
            await PickingSectionNavigation.SwitchAsync(this, _services, _assembly,
                _process, section);
        }
        finally { SetBusy(false); await UpdateCameraAsync(); }
    }

    private async void OnCloseClicked(object? sender, EventArgs e)
    {
        if (_busy || _process.HasPendingCommand || _assembly is null || !CanWork) return;
        SetBusy(true);
        try
        {
            var check = await _client.CheckClosureAsync(_assembly.Id, _assembly.WarehouseId);
            if (!check.CanOperatorClose)
            {
                ErrorLabel.Text = string.Join("\n", check.HardBlocks.Concat(check.ManagerIssues));
                if (string.IsNullOrWhiteSpace(ErrorLabel.Text))
                    ErrorLabel.Text = "Сборка требует решения руководителя склада.";
                await DisplayAlertAsync(check.HardBlocks.Count > 0
                        ? "Закрытие пока невозможно" : "Требуется решение руководителя",
                    ErrorLabel.Text + (check.ManagerIssues.Count > 0
                        ? "\n\nСверьте сборку с 1С и примите решение в веб-приложении." : ""),
                    "Понятно");
                return;
            }
            if (!await DisplayAlertAsync("Закрыть и отгрузить",
                "Складской факт будет проведён. Продолжить?", "Закрыть", "Отмена")) return;
            _orderingReplenishment = false;
            await RunWorkAsync(() => _process.CloseAsync(_assembly.Id,
                _assembly.WarehouseId, check.Revision));
        }
        catch (MobileApiException exception) { ErrorLabel.Text = exception.Message; }
        catch (HttpRequestException) { ErrorLabel.Text = "Не удалось сверить документы 1С."; }
        finally { SetBusy(false); await UpdateCameraAsync(); }
    }

    private async void OnRetryClicked(object? sender, EventArgs e)
    {
        if (_busy || !_process.HasPendingCommand) return;
        await RunWorkAsync(_process.RetryAsync);
    }

    private async Task RunWorkAsync(Func<Task<MobilePickingAssemblyResponse>> execute)
    {
        using var measurement = MobilePerformanceDiagnostics.Action(_orderingReplenishment ? "Заказ подпитки" : "Закрытие и отгрузка");
        SetBusy(true);
        ErrorLabel.Text = string.Empty;
        MobilePickingAssemblyResponse? refreshRoute = null;
        try
        {
            var assembly = await execute();
            ApplyAssembly(assembly);
            if (_orderingReplenishment)
                refreshRoute = assembly;
            else if (assembly.Status == MobilePickingAssemblyStatus.Closed)
                await Navigation.PopAsync();
            else
                await PickingSectionNavigation.SwitchAsync(this, _services, assembly,
                    _process, PickingSection.Sku);
        }
        catch (MobileApiException exception) { ErrorLabel.Text = exception.Message; }
        catch (HttpRequestException)
        {
            ErrorLabel.Text = _process.HasPendingCommand
                ? "Ответ не подтверждён. Повторите ту же команду."
                : "Команда выполнена, но экран не обновился. Потяните вниз.";
        }
        finally { SetBusy(false); }
        if (refreshRoute is not null) await RefreshAsync(refreshRoute);
        else await UpdateCameraAsync();
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        Interlocked.Increment(ref _scanGeneration);
        ProgressIndicator.IsVisible = busy && !RouteRefreshView.IsRefreshing;
        ProgressIndicator.IsRunning = ProgressIndicator.IsVisible;
        RetryButton.IsVisible = !busy && _process.HasPendingCommand;
        if (_process.HasPendingCommand) RetryButton.Text = $"Повторить: {_process.PendingLabel}";
        var canNavigate = !busy && !_process.HasPendingCommand && _assembly is not null;
        SkuButton.IsEnabled = canNavigate;
        SourcesButton.IsEnabled = canNavigate;
        CloseButton.IsVisible = CanWork;
        CloseButton.IsEnabled = canNavigate && CanWork;
        NavigationPage.SetHasBackButton(this, !busy && !_process.HasPendingCommand);
        if (busy) CameraScannerView.Stop();
    }

    private async Task UpdateCameraAsync()
    {
        if (_visible && !_busy && !_process.HasPendingCommand && CanWork && RouteHint.IsVisible
            && _scanner.ActiveSource == BarcodeScanSource.Camera)
            await CameraScannerView.StartAsync();
        else CameraScannerView.Stop();
    }

    protected override bool OnBackButtonPressed()
    {
        if (!_busy && !_process.HasPendingCommand) return base.OnBackButtonPressed();
        ErrorLabel.Text = "Сначала завершите текущую команду.";
        return true;
    }
}

public sealed record PickingRouteRow(Guid SkuId, string? LocationCode,
    bool DirectPalletPick, bool IsCurrent, string Heading,
    string PlanText, string ReservedText, string FactText,
    string StepQuantity, string Detail)
{
    public string? Address { get; init; } = LocationCode;
    public bool HasExpiredStock { get; init; }
    public PickingStockSummary StockSummary { get; init; }
    public PickingReplenishmentAction Replenishment { get; init; } = PickingReplenishmentAction.Hidden;
    public bool HasStep => LocationCode is not null;
    public bool CanChooseCell => !HasStep;
    public string ZoneIcon => DirectPalletPick ? "picking_pallet.png" : "picking_shelf.png";
    public string ZoneName => DirectPalletPick ? "Хранение" : "Комплектация";
    public bool HasDetail => !string.IsNullOrWhiteSpace(Detail);
}

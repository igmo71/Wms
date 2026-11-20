using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Wms.Contracts.Mobile.V1;
using Wms.Mobile.Scanning;
using Wms.Mobile.Services;

namespace Wms.Mobile;

public partial class PickingRoutePage : ContentPage
{
    private readonly MobilePickingClient _client;
    private readonly IOperationalBarcodeScanner _scanner;
    private readonly IServiceProvider _services;
    private PickingAssemblyProcess _process;
    private MobilePickingAssemblyResponse? _assembly;
    private bool _busy;
    private bool _visible;
    private long _scanGeneration;

    public PickingRoutePage(MobilePickingClient client,
        IOperationalBarcodeScanner scanner,
        PickingAssemblyProcess process,
        IServiceProvider services)
    {
        InitializeComponent();
        _client = client;
        _scanner = scanner;
        _process = process;
        _services = services;
        BindingContext = this;
        CameraScannerView.Configure(scanner);
    }

    public IReadOnlyList<PickingRouteRow> Steps { get; private set; } = [];

    public void Show(MobilePickingAssemblyResponse assembly, PickingAssemblyProcess process)
    {
        _assembly = assembly;
        _process = process;
        ApplyHeading(assembly);
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

    private async Task RefreshAsync()
    {
        if (_busy || _assembly is null) return;
        using var measurement = MobilePerformanceDiagnostics.Load("Маршрут");
        SetBusy(true);
        try
        {
            var assembly = await _client.GetAsync(_assembly.Id, _assembly.WarehouseId);
            var route = await _client.RouteAsync(assembly.Id, assembly.WarehouseId);
            _assembly = assembly;
            ApplyHeading(assembly);
            var work = route.Work;
            var skus = work.Skus.ToDictionary(x => x.SkuId);
            var available = route.Steps.GroupBy(x => x.SkuId)
                .ToDictionary(x => x.Key, x => x.Sum(step => step.Quantity));
            Steps = route.Steps.Select((x, index) =>
            {
                var sku = skus.GetValueOrDefault(x.SkuId);
                var name = sku?.Name ?? sku?.Code ?? x.SkuId.ToString();
                var plan = sku is null ? "—" : sku.PlannedQuantity.ToString("0.###", CultureInfo.CurrentCulture);
                var fact = sku is null ? "—" : sku.ConfirmedQuantity.ToString("0.###", CultureInfo.CurrentCulture);
                return new PickingRouteRow(x.SkuId, x.LocationCode,
                    x.DirectPalletPick, index == 0,
                    $"{index + 1}. {name}",
                    $"План {plan} {sku?.Unit ?? "—"}",
                    $"Доступно {available[x.SkuId]:0.###}",
                    $"Факт {fact}",
                    $"В этой ячейке: {x.Quantity.ToString("0.###", CultureInfo.CurrentCulture)}",
                    (x.ExpirationDate is DateOnly date ? $"до {date:dd.MM.yyyy}" : "")
                    + (x.DirectPalletPick ? " · снять весь поддон" : ""))
                { Address = string.IsNullOrWhiteSpace(x.LocationName) ? x.LocationCode : x.LocationName };
            }).ToArray();
            OnPropertyChanged(nameof(Steps));
            EmptyLabel.IsVisible = Steps.Count == 0;
            ErrorLabel.Text = string.Empty;
        }
        catch (MobileApiException exception)
        {
            Steps = [];
            OnPropertyChanged(nameof(Steps));
            EmptyLabel.IsVisible = true;
            ErrorLabel.Text = exception.Message;
        }
        catch (HttpRequestException)
        {
            Steps = [];
            OnPropertyChanged(nameof(Steps));
            EmptyLabel.IsVisible = true;
            ErrorLabel.Text = "Не удалось обновить маршрут.";
        }
        finally { SetBusy(false); await UpdateCameraAsync(); }
    }

    private void ApplyHeading(MobilePickingAssemblyResponse assembly)
    {
        PickingTitle.Set(HeaderTitleLabel, "Отбор", assembly.Number);
        Title = $"Отбор · {assembly.Number}";
        StatusLabel.Text = assembly.Status switch
        {
            MobilePickingAssemblyStatus.Draft => "Черновик",
            MobilePickingAssemblyStatus.Preparing => "Запрос в 1С КОтбору",
            MobilePickingAssemblyStatus.InProgress => "В работе",
            MobilePickingAssemblyStatus.Closed => "Сборка закрыта",
            _ => "Сборка отменяется или отменена"
        };
        OwnerLabel.Text = assembly.ResponsibleDisplayName;
    }

    private async void OnStepTapped(object? sender, TappedEventArgs e)
    {
        if (_busy || _process.HasPendingCommand || _assembly is null
            || e.Parameter is not PickingRouteRow row) return;
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
        if (!_visible || _busy || _process.HasPendingCommand || _assembly is null
            || string.IsNullOrWhiteSpace(code)) return;
        var scannedCode = code.Trim();
        var row = Steps.FirstOrDefault(x => string.Equals(x.LocationCode,
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
        if (_assembly is null || _busy || _process.HasPendingCommand) return;
        SetBusy(true);
        ErrorLabel.Text = string.Empty;
        try
        {
            var page = _services.GetRequiredService<PickingEntryPage>();
            page.ShowPick(_assembly, _process, row.SkuId,
                row.LocationCode, row.DirectPalletPick, PickingEntryOrigin.Route, scannedCode);
            await Navigation.PushAsync(page);
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
        if (_busy || _process.HasPendingCommand || _assembly is null) return;
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
            SetBusy(false);
            await CloseWorkAsync(() => _process.CloseAsync(_assembly.Id,
                _assembly.WarehouseId, check.Revision));
        }
        catch (MobileApiException exception) { ErrorLabel.Text = exception.Message; }
        catch (HttpRequestException) { ErrorLabel.Text = "Не удалось сверить документы 1С."; }
        finally { SetBusy(false); await UpdateCameraAsync(); }
    }

    private async void OnRetryClicked(object? sender, EventArgs e)
    {
        if (_busy || !_process.HasPendingCommand) return;
        await CloseWorkAsync(_process.RetryAsync);
    }

    private async Task CloseWorkAsync(Func<Task<MobilePickingAssemblyResponse>> execute)
    {
        SetBusy(true);
        try
        {
            _assembly = await execute();
            await PickingSectionNavigation.SwitchAsync(this, _services, _assembly,
                _process, PickingSection.Sku);
        }
        catch (MobileApiException exception) { ErrorLabel.Text = exception.Message; }
        catch (HttpRequestException)
        {
            ErrorLabel.Text = _process.HasPendingCommand
                ? "Ответ не подтверждён. Повторите ту же команду."
                : "Сборка закрыта, но экран не обновился. Вернитесь к SKU.";
        }
        finally { SetBusy(false); await UpdateCameraAsync(); }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        Interlocked.Increment(ref _scanGeneration);
        ProgressIndicator.IsVisible = busy && !RouteRefreshView.IsRefreshing;
        ProgressIndicator.IsRunning = ProgressIndicator.IsVisible;
        RetryButton.IsVisible = !busy && _process.HasPendingCommand;
        var canNavigate = !busy && !_process.HasPendingCommand && _assembly is not null;
        SkuButton.IsEnabled = canNavigate;
        SourcesButton.IsEnabled = canNavigate;
        CloseButton.IsEnabled = canNavigate;
        NavigationPage.SetHasBackButton(this, !busy && !_process.HasPendingCommand);
        if (busy) CameraScannerView.Stop();
    }

    private async Task UpdateCameraAsync()
    {
        if (_visible && !_busy && !_process.HasPendingCommand
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

public sealed record PickingRouteRow(Guid SkuId, string LocationCode,
    bool DirectPalletPick, bool IsCurrent, string Heading,
    string PlanText, string AvailableText, string FactText,
    string StepQuantity, string Detail)
{
    public string Address { get; init; } = LocationCode;
    public bool HasDetail => !string.IsNullOrWhiteSpace(Detail);
}

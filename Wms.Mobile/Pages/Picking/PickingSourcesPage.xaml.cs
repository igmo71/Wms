using Microsoft.Extensions.DependencyInjection;
using Wms.Contracts.Mobile.V1;
using Wms.Mobile.Scanning;
using Wms.Mobile.Services;

namespace Wms.Mobile;

public partial class PickingSourcesPage : ContentPage
{
    private readonly MobilePickingClient _client;
    private readonly IOperationalBarcodeScanner _scanner;
    private readonly MobileWarehouseSelection _selection;
    private readonly IServiceProvider _services;
    private PickingAssemblyProcess _process;
    private MobilePickingAssemblyResponse? _assembly;
    private Guid _warehouseId;
    private bool _visible;
    private bool _busy;
    private bool _useSuppliedAssembly;
    private long _scanGeneration;

    public PickingSourcesPage(MobilePickingClient client,
        IOperationalBarcodeScanner scanner, MobileWarehouseSelection selection,
        PickingAssemblyProcess process, IServiceProvider services)
    {
        InitializeComponent();
        _client = client;
        _scanner = scanner;
        _selection = selection;
        _process = process;
        _services = services;
        BindingContext = this;
        CameraScannerView.Configure(scanner);
    }

    public IReadOnlyList<PickingSourceRow> SourceRows { get; private set; } = [];
    private bool IsOwner => _assembly?.ResponsibleUserId == _selection.CurrentUserId;
    private bool CanAttach => _assembly is null
        || _assembly.Status == MobilePickingAssemblyStatus.Draft && IsOwner;

    public void ShowForCreation(Guid warehouseId, PickingAssemblyProcess process)
    {
        _warehouseId = warehouseId;
        _process = process;
        _assembly = null;
        _useSuppliedAssembly = false;
        Apply(null);
    }

    public void Show(MobilePickingAssemblyResponse assembly, PickingAssemblyProcess process)
    {
        _warehouseId = assembly.WarehouseId;
        _process = process;
        _useSuppliedAssembly = true;
        Apply(assembly);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _visible = true;
        _scanner.ScanReceived += OnScanReceived;
        if (_useSuppliedAssembly)
            _useSuppliedAssembly = false;
        else await RefreshAsync();
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
        SourcesRefreshView.IsRefreshing = false;
    }

    private async Task RefreshAsync()
    {
        if (_busy) return;
        if (_assembly is null)
        {
            Apply(null);
            await UpdateCameraAsync();
            return;
        }
        SetBusy(true);
        try
        {
            Apply(await _client.GetAsync(_assembly.Id, _warehouseId));
            if (!_process.HasPendingCommand) ErrorLabel.Text = string.Empty;
        }
        catch (MobileApiException exception) { ErrorLabel.Text = exception.Message; }
        catch (HttpRequestException) { ErrorLabel.Text = "Не удалось обновить документы 1С."; }
        finally { SetBusy(false); await UpdateCameraAsync(); }
    }

    private void Apply(MobilePickingAssemblyResponse? assembly)
    {
        _assembly = assembly;
        NumberLabel.Text = assembly is null ? "Новая сборка" : $"Сборка {assembly.Number}";
        StatusLabel.Text = assembly is null ? "Документы 1С ещё не добавлены."
            : assembly.Status switch
            {
                MobilePickingAssemblyStatus.Draft => "Черновик",
                MobilePickingAssemblyStatus.Preparing => "Ожидается перевод РО 1С в КОтбору",
                MobilePickingAssemblyStatus.InProgress => "Отбор в работе",
                MobilePickingAssemblyStatus.Closed => "Сборка закрыта",
                _ => "Сборка отменяется или отменена"
            };
        OwnerLabel.Text = assembly is null ? string.Empty
            : $"Ответственный: {assembly.ResponsibleDisplayName}";
        SourceRows = assembly?.Sources.Select(x => new PickingSourceRow(
            x.SourceRefKey, x.Number ?? x.SourceRefKey.ToString(),
            x.StartConfirmedAtUtc is not null ? "КОтбору подтверждено"
                : x.LastStartAttemptAtUtc is not null ? "Переход не подтверждён"
                : x.Status,
            x.LastStartError ?? x.LastCheckError,
            assembly.Status == MobilePickingAssemblyStatus.Draft && IsOwner)).ToArray() ?? [];
        OnPropertyChanged(nameof(SourceRows));
        EmptyLabel.IsVisible = SourceRows.Count == 0;
        ScanHintLabel.Text = CanAttach
            ? "Сканируйте РО 1С, чтобы добавить его в сборку."
            : "Состав этой сборки уже зафиксирован.";
        UpdateActions();
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
        if (!_visible || _busy || _process.HasPendingCommand || !CanAttach) return;
        SetBusy(true);
        ErrorLabel.Text = string.Empty;
        try
        {
            var resolved = await _client.ResolveAsync(_warehouseId, barcode);
            if (resolved.Assembly is { } linked)
            {
                if (_assembly?.Id == linked.Id)
                    ErrorLabel.Text = "РО 1С уже находится в этой сборке.";
                else if (await DisplayAlertAsync("РО 1С уже в сборке",
                    $"Документ относится к сборке {linked.Number}. Открыть её?",
                    "Открыть", "Остаться"))
                    await OpenSkuAsync(linked);
                return;
            }
            if (resolved.FreeSource is null)
            {
                ErrorLabel.Text = "РО 1С не найден.";
                return;
            }
            Apply(_assembly is null
                ? await _process.CreateAsync(_warehouseId, barcode)
                : await _process.AttachAsync(_assembly.Id, _warehouseId, barcode));
        }
        catch (MobileApiException exception) { ErrorLabel.Text = exception.Message; }
        catch (HttpRequestException)
        {
            ErrorLabel.Text = _process.HasPendingCommand
                ? "Ответ не подтверждён. Повторите ту же команду."
                : "Сервер WMS недоступен. Повторите сканирование.";
        }
        finally { SetBusy(false); await UpdateCameraAsync(); }
    }

    private async void OnDetachClicked(object? sender, EventArgs e)
    {
        if (_busy || _process.HasPendingCommand || _assembly is null
            || _assembly.Status != MobilePickingAssemblyStatus.Draft || !IsOwner
            || sender is not Button { CommandParameter: Guid sourceRefKey }) return;
        if (!await DisplayAlertAsync("Открепить РО 1С",
            "Документ будет исключён из плана сборки. Продолжить?",
            "Открепить", "Отмена")) return;
        await RunCommandAsync(() => _process.DetachAsync(
            _assembly.Id, sourceRefKey, _warehouseId));
    }

    private async void OnStartClicked(object? sender, EventArgs e)
    {
        if (_busy || _process.HasPendingCommand || _assembly is null || !IsOwner) return;
        await RunCommandAsync(() => _process.StartAsync(_assembly.Id, _warehouseId),
            openSkuAfterStart: true);
    }

    private async void OnTakeOverClicked(object? sender, EventArgs e)
    {
        if (_busy || _process.HasPendingCommand || _assembly is null || IsOwner) return;
        if (!await DisplayAlertAsync("Принять сборку",
            $"Сейчас за сборку отвечает {_assembly.ResponsibleDisplayName}. Принять её на себя?",
            "Принять", "Отмена")) return;
        await RunCommandAsync(() => _process.TakeOverAsync(
            _assembly.Id, _warehouseId, _assembly.Revision));
    }

    private async void OnRetryClicked(object? sender, EventArgs e)
    {
        if (_busy || !_process.HasPendingCommand) return;
        var wasStart = _process.PendingLabel == "Начать Отбор";
        await RunCommandAsync(_process.RetryAsync, wasStart, retry: true);
    }

    private async Task RunCommandAsync(Func<Task<MobilePickingAssemblyResponse>> action,
        bool openSkuAfterStart = false, bool retry = false)
    {
        if (_busy || _process.HasPendingCommand && !retry) return;
        SetBusy(true);
        ErrorLabel.Text = string.Empty;
        try
        {
            var result = await action();
            Apply(result);
            if (openSkuAfterStart && result.Status == MobilePickingAssemblyStatus.InProgress)
                await OpenSkuAsync(result);
            else if (openSkuAfterStart && _process.LastStartResult is { } start)
                ErrorLabel.Text = string.Join("\n", start.Sources
                    .Where(x => x.Error is not null)
                    .Select(x => x.Error));
        }
        catch (MobileApiException exception) { ErrorLabel.Text = exception.Message; }
        catch (HttpRequestException)
        {
            ErrorLabel.Text = _process.HasPendingCommand
                ? "Ответ не подтверждён. Повторите ту же команду."
                : "Действие выполнено, но экран не обновился. Потяните вниз.";
        }
        finally { SetBusy(false); await UpdateCameraAsync(); }
    }

    private async void OnSkuClicked(object? sender, EventArgs e)
    {
        if (_busy || _process.HasPendingCommand || _assembly is null) return;
        await OpenSkuAsync(_assembly);
    }

    private async Task OpenSkuAsync(MobilePickingAssemblyResponse assembly)
    {
        var page = _services.GetRequiredService<PickingSkuPage>();
        page.Show(assembly, _process);
        await Navigation.PushAsync(page);
        Navigation.RemovePage(this);
    }

    private void UpdateActions()
    {
        var pending = _process.HasPendingCommand;
        RetryButton.IsVisible = !_busy && pending;
        if (pending) RetryButton.Text = $"Повторить: {_process.PendingLabel}";
        StartButton.IsVisible = _assembly is { Status: MobilePickingAssemblyStatus.Draft
            or MobilePickingAssemblyStatus.Preparing } && IsOwner;
        if (_assembly is not null)
            StartButton.Text = _assembly.Status == MobilePickingAssemblyStatus.Preparing
                ? "Повторить начало" : "Начать Отбор";
        StartButton.IsEnabled = !_busy && !pending;
        TakeOverButton.IsVisible = _assembly is { Status: MobilePickingAssemblyStatus.Preparing
            or MobilePickingAssemblyStatus.InProgress } && !IsOwner;
        TakeOverButton.IsEnabled = !_busy && !pending;
        SkuButton.IsVisible = _assembly is not null;
        SkuButton.IsEnabled = !_busy && !pending;
        NavigationPage.SetHasBackButton(this, !_busy && !pending);
    }

    private void SetBusy(bool busy)
    {
        Interlocked.Increment(ref _scanGeneration);
        _busy = busy;
        ProgressIndicator.IsVisible = busy;
        UpdateActions();
        if (busy) CameraScannerView.Stop();
    }

    private async Task UpdateCameraAsync()
    {
        if (_visible && !_busy && !_process.HasPendingCommand && CanAttach
            && _scanner.ActiveSource == BarcodeScanSource.Camera)
            await CameraScannerView.StartAsync();
        else CameraScannerView.Stop();
    }

    protected override bool OnBackButtonPressed()
    {
        if (!_busy && !_process.HasPendingCommand) return base.OnBackButtonPressed();
        ErrorLabel.Text = "Сначала завершите повтор команды.";
        return true;
    }
}

public sealed record PickingSourceRow(Guid SourceRefKey, string NumberText,
    string StatusText, string? ErrorText, bool CanDetach)
{
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorText);
}

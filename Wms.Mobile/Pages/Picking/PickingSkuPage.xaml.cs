using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Wms.Contracts.Mobile.V1;
using Wms.Mobile.Services;

namespace Wms.Mobile;

public partial class PickingSkuPage : ContentPage
{
    private readonly MobilePickingClient _client;
    private readonly MobileReplenishmentClient _replenishments;
    private readonly MobileWarehouseSelection _selection;
    private readonly IServiceProvider _services;
    private PickingAssemblyProcess _process;
    private MobilePickingAssemblyResponse? _assembly;
    private MobilePickingWorkResponse? _work;
    private bool _busy;
    private bool _useSuppliedAssembly;
    private IReadOnlyDictionary<Guid, decimal>? _availableBySku;
    private MobilePickingReplenishmentsResponse? _replenishmentList;

    public PickingSkuPage(MobilePickingClient client,
        MobileReplenishmentClient replenishments,
        MobileWarehouseSelection selection, PickingAssemblyProcess process,
        IServiceProvider services)
    {
        InitializeComponent();
        _client = client;
        _replenishments = replenishments;
        _selection = selection;
        _process = process;
        _services = services;
        BindingContext = this;
    }

    public IReadOnlyList<PickingSkuPlanRow> SkuRows { get; private set; } = [];
    private bool IsOwner => _assembly?.ResponsibleUserId == _selection.CurrentUserId;

    public void Show(MobilePickingAssemblyResponse assembly, PickingAssemblyProcess process,
        bool useSuppliedAssembly = false)
    {
        _process = process;
        _useSuppliedAssembly = useSuppliedAssembly;
        _work = null;
        _availableBySku = null;
        _replenishmentList = null;
        Apply(assembly);
    }

    internal void UseFreshAssemblyOnReturn(MobilePickingAssemblyResponse assembly)
    {
        if (_assembly is not { } current || current.Id != assembly.Id
            || current.WarehouseId != assembly.WarehouseId) return;
        _assembly = assembly;
        _useSuppliedAssembly = true;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        var useSuppliedAssembly = _useSuppliedAssembly;
        _useSuppliedAssembly = false;
        await RefreshAsync(useSuppliedAssembly);
    }

    private async void OnRefreshing(object? sender, EventArgs e)
    {
        await RefreshAsync();
        SkuRefreshView.IsRefreshing = false;
    }

    private async Task RefreshAsync(bool useSuppliedAssembly = false)
    {
        if (_busy || _assembly is null) return;
        using var measurement = MobilePerformanceDiagnostics.Load("SKU");
        SetBusy(true);
        try
        {
            var assembly = useSuppliedAssembly ? _assembly
                : await _client.GetAsync(_assembly.Id, _assembly.WarehouseId);
            _work = null;
            _availableBySku = null;
            _replenishmentList = null;
            string? replenishmentError = null;
            if (assembly.Status == MobilePickingAssemblyStatus.InProgress
                && assembly.ResponsibleUserId == _selection.CurrentUserId)
            {
                try
                {
                    var list = await _replenishments.AssemblyAsync(assembly.Id,
                        assembly.WarehouseId);
                    _work = list.Work;
                    _availableBySku = list.PickableBySku;
                    _replenishmentList = list;
                }
                catch (MobileApiException exception)
                { replenishmentError = exception.Message; }
                catch (HttpRequestException)
                { replenishmentError = "Не удалось обновить Подпитки."; }
            }
            _work ??= await _client.WorkAsync(assembly.Id, assembly.WarehouseId);
            Apply(assembly);
            if (!_process.HasPendingCommand)
                ErrorLabel.Text = replenishmentError ?? string.Empty;
        }
        catch (MobileApiException exception) { ErrorLabel.Text = exception.Message; }
        catch (HttpRequestException) { ErrorLabel.Text = "Не удалось обновить сборку."; }
        finally { SetBusy(false); }
    }

    private void Apply(MobilePickingAssemblyResponse assembly)
    {
        _assembly = assembly;
        PickingTitle.Set(HeaderTitleLabel, "Отбор", assembly.Number);
        Title = $"Отбор · {assembly.Number}";
        StatusLabel.Text = assembly.Status switch
        {
            MobilePickingAssemblyStatus.Draft => "Черновик",
            MobilePickingAssemblyStatus.Preparing => "Запрос в 1С КОтбору",
            MobilePickingAssemblyStatus.InProgress => "В работе",
            MobilePickingAssemblyStatus.Closed => assembly.Sources.All(x =>
                x.ExchangeStage == MobileShippingExchangeStage.PostConfirmed)
                ? "Сборка закрыта · обмен с 1С подтверждён"
                : "Складской факт проведён · обмен с 1С ожидается",
            _ => "Сборка отменяется или отменена"
        };
        OwnerLabel.Text = assembly.ResponsibleDisplayName;
        var entriesBySku = _work?.Entries.ToLookup(x => x.SkuId);
        var proposalSkuIds = _replenishmentList?.Proposals.Select(x => x.SkuId).ToHashSet();
        SkuRows = _work?.Skus.Select(x => new PickingSkuPlanRow(
            x.SkuId, x.Name ?? x.Code ?? x.SkuId.ToString(),
            x.PlannedQuantity, x.ConfirmedQuantity, x.Unit)
            {
                Entries = entriesBySku?[x.SkuId].Select(entry => new PickingEntryListRow(
                    entry.Id,
                    string.IsNullOrWhiteSpace(entry.LocationName) ? entry.LocationCode : entry.LocationName,
                    entry.Quantity, entry.ExpirationDate,
                    assembly.Status == MobilePickingAssemblyStatus.InProgress && IsOwner))
                    .ToArray() ?? [],
                AvailableQuantity = _availableBySku?.GetValueOrDefault(x.SkuId),
                ReplenishmentState = ReplenishmentStateFor(x.SkuId,
                    x.PlannedQuantity - x.ConfirmedQuantity,
                    _availableBySku?.GetValueOrDefault(x.SkuId),
                    proposalSkuIds?.Contains(x.SkuId) == true),
                HasReplenishmentProposal = proposalSkuIds?.Contains(x.SkuId) == true
            }).ToArray()
            ?? assembly.PlanLines.GroupBy(x => x.SkuId)
                .Select(group => new PickingSkuPlanRow(group.Key,
                    group.First().SkuName ?? group.First().SkuCode ?? group.Key.ToString(),
                    group.Sum(x => x.Quantity), 0, null))
                .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(x => x.SkuId).ToArray();
        OnPropertyChanged(nameof(SkuRows));
        UpdateActions();
    }

    private PickingReplenishmentState ReplenishmentStateFor(Guid skuId,
        decimal remaining, decimal? available, bool hasProposal)
    {
        if (remaining <= 0 || _assembly?.Status != MobilePickingAssemblyStatus.InProgress
            || !IsOwner || available is null || _replenishmentList is null)
            return PickingReplenishmentState.Hidden;
        if (available >= remaining)
            return _replenishmentList.Tasks.Any(x => x.SkuId == skuId
                && x.Status != MobileReplenishmentTaskStatus.Cancelled)
                ? PickingReplenishmentState.Ready
                : PickingReplenishmentState.Hidden;
        if (_replenishmentList.Tasks.Any(x => x.SkuId == skuId
            && x.Status == MobileReplenishmentTaskStatus.InProgress
            && x.TransferredQuantity < x.OrderedQuantity))
            return PickingReplenishmentState.InProgress;
        if (_replenishmentList.Tasks.Any(x => x.SkuId == skuId
            && x.Status == MobileReplenishmentTaskStatus.Ordered
            && x.TransferredQuantity < x.OrderedQuantity))
            return PickingReplenishmentState.Ordered;
        return hasProposal ? PickingReplenishmentState.CanOrder : PickingReplenishmentState.Unavailable;
    }

    private async void OnSkuTapped(object? sender, TappedEventArgs e)
    {
        if (_busy || _process.HasPendingCommand || _assembly is null
            || _assembly.Status != MobilePickingAssemblyStatus.InProgress
            || !IsOwner || e.Parameter is not Guid skuId) return;
        var row = SkuRows.FirstOrDefault(x => x.SkuId == skuId);
        if (row is null || row.PlannedQuantity <= row.ConfirmedQuantity) return;
        var page = _services.GetRequiredService<PickingEntryPage>();
        page.ShowPick(_assembly, _process, skuId, null, false, PickingEntryOrigin.Sku);
        await Navigation.PushAsync(page);
    }

    private async void OnSkuInfoClicked(object? sender, EventArgs e)
    {
        if (_busy || _process.HasPendingCommand || _assembly is null
            || sender is not Button { CommandParameter: Guid skuId }) return;
        var row = SkuRows.FirstOrDefault(x => x.SkuId == skuId);
        if (row is null) return;
        var page = _services.GetRequiredService<SkuLocationsPage>();
        page.Show(_assembly.WarehouseId, skuId, row.Name, row.Unit);
        await Navigation.PushAsync(page);
    }

    private async void OnReturnClicked(object? sender, EventArgs e)
    {
        if (_busy || _process.HasPendingCommand || _assembly is null
            || _assembly.Status != MobilePickingAssemblyStatus.InProgress
            || !IsOwner || sender is not Button { CommandParameter: Guid entryId }) return;
        var entry = _work?.Entries.SingleOrDefault(x => x.Id == entryId);
        if (entry is null) return;
        var page = _services.GetRequiredService<PickingEntryPage>();
        page.ShowReturn(_assembly, _process, entry);
        await Navigation.PushAsync(page);
    }

    private async void OnRouteClicked(object? sender, EventArgs e)
    {
        if (_busy || _process.HasPendingCommand || _assembly is null
            || _assembly.Status != MobilePickingAssemblyStatus.InProgress || !IsOwner) return;
        await SwitchSectionAsync(PickingSection.Route);
    }

    private async void OnReplenishmentClicked(object? sender, EventArgs e)
    {
        if (_busy || _process.HasPendingCommand || _assembly is null
            || _assembly.Status != MobilePickingAssemblyStatus.InProgress || !IsOwner
            || sender is not Button { CommandParameter: Guid skuId }) return;
        var row = SkuRows.FirstOrDefault(x => x.SkuId == skuId);
        if (row is null || !row.HasReplenishmentIcon) return;
        if (row.CanOrderMoreReplenishment)
        {
            if (!await DisplayAlertAsync("Подпитка", row.ReplenishmentHint,
                "Заказать ещё", "Закрыть")) return;
        }
        else if (row.ReplenishmentState != PickingReplenishmentState.CanOrder)
        {
            await DisplayAlertAsync("Подпитка", row.ReplenishmentHint, "Понятно");
            return;
        }

        if (_replenishmentList is not { } list) return;
        await RunCommandAsync(() => _process.OrderReplenishmentAsync(
            _assembly.Id, _assembly.WarehouseId, list.Revision,
            skuId), operation: "Заказ подпитки");
    }

    private async void OnCloseClicked(object? sender, EventArgs e)
    {
        if (_busy || _process.HasPendingCommand || _assembly is null
            || _assembly.Status is not (MobilePickingAssemblyStatus.InProgress
                or MobilePickingAssemblyStatus.Closed)
            || _assembly.Status == MobilePickingAssemblyStatus.InProgress && !IsOwner)
            return;
        SetBusy(true);
        try
        {
            if (_assembly.Status == MobilePickingAssemblyStatus.Closed)
            {
                var assemblyId = _assembly.Id;
                var warehouseId = _assembly.WarehouseId;
                var revision = _assembly.Revision;
                SetBusy(false);
                await RunCommandAsync(() => _process.CloseAsync(assemblyId,
                    warehouseId, revision), operation: "Закрытие и отгрузка");
                return;
            }
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
            await RunCommandAsync(() => _process.CloseAsync(_assembly.Id,
                _assembly.WarehouseId, check.Revision), operation: "Закрытие и отгрузка");
        }
        catch (MobileApiException exception) { ErrorLabel.Text = exception.Message; }
        catch (HttpRequestException) { ErrorLabel.Text = "Не удалось сверить документы 1С."; }
        finally { SetBusy(false); }
    }

    private async void OnSourcesClicked(object? sender, EventArgs e)
    {
        if (_busy || _process.HasPendingCommand || _assembly is null) return;
        await SwitchSectionAsync(PickingSection.Sources);
    }

    private async Task SwitchSectionAsync(PickingSection section)
    {
        if (_assembly is null) return;
        SetBusy(true);
        try
        {
            await PickingSectionNavigation.SwitchAsync(this, _services, _assembly,
                _process, section);
        }
        finally { SetBusy(false); }
    }

    private async void OnStartClicked(object? sender, EventArgs e)
    {
        if (_busy || _process.HasPendingCommand || _assembly is null || !IsOwner) return;
        await RunCommandAsync(() => _process.StartAsync(
            _assembly.Id, _assembly.WarehouseId), operation: "Начало отбора");
    }

    private async void OnTakeOverClicked(object? sender, EventArgs e)
    {
        if (_busy || _process.HasPendingCommand || _assembly is null || IsOwner) return;
        if (!await DisplayAlertAsync("Принять сборку",
            $"Сейчас за сборку отвечает {_assembly.ResponsibleDisplayName}. Принять её на себя?",
            "Принять", "Отмена")) return;
        await RunCommandAsync(() => _process.TakeOverAsync(
            _assembly.Id, _assembly.WarehouseId, _assembly.Revision), operation: "Принятие сборки");
    }

    private async void OnRetryClicked(object? sender, EventArgs e)
    {
        if (_busy || !_process.HasPendingCommand) return;
        await RunCommandAsync(_process.RetryAsync, retry: true,
            operation: $"Повтор: {_process.PendingLabel}");
    }

    private async Task RunCommandAsync(Func<Task<MobilePickingAssemblyResponse>> action,
        bool retry = false, string operation = "Команда")
    {
        if (_busy || _process.HasPendingCommand && !retry) return;
        using var measurement = MobilePerformanceDiagnostics.Action(operation);
        SetBusy(true);
        ErrorLabel.Text = string.Empty;
        var refreshWork = false;
        try
        {
            var assembly = await action();
            if (_assembly?.Status == MobilePickingAssemblyStatus.InProgress
                && assembly.Status == MobilePickingAssemblyStatus.Closed
                && _process.LastCloseResult?.AssemblyId == assembly.Id)
            {
                await Navigation.PopAsync();
                return;
            }
            refreshWork = assembly.Status == MobilePickingAssemblyStatus.InProgress
                && assembly.ResponsibleUserId == _selection.CurrentUserId;
            _work = refreshWork ? null
                : await _client.WorkAsync(assembly.Id, assembly.WarehouseId);
            _availableBySku = null;
            _replenishmentList = null;
            Apply(assembly);
            if (_process.LastStartResult is { Status: MobilePickingAssemblyStatus.Preparing } start)
                ErrorLabel.Text = string.Join("\n", start.Sources
                    .Where(x => x.Error is not null).Select(x => x.Error));
            if (_process.LastCloseResult is { AllConfirmed: false } close)
                ErrorLabel.Text = string.Join("\n", close.Documents
                    .Where(x => x.Error is not null)
                    .Select(x => $"{assembly.Sources.FirstOrDefault(s => s.SourceRefKey == x.SourceRefKey)?.Number ?? x.SourceRefKey.ToString()}: {x.Error}"));
        }
        catch (MobileApiException exception) { ErrorLabel.Text = exception.Message; }
        catch (HttpRequestException)
        {
            ErrorLabel.Text = _process.HasPendingCommand
                ? "Ответ не подтверждён. Повторите ту же команду."
                : "Действие выполнено, но экран не обновился. Потяните вниз.";
        }
        finally { SetBusy(false); }
        if (refreshWork) await RefreshAsync(useSuppliedAssembly: true);
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
        SourcesButton.IsEnabled = !_busy && !pending;
        var canWork = _assembly is { Status: MobilePickingAssemblyStatus.InProgress }
            && IsOwner;
        RouteButton.IsEnabled = !_busy && !pending && canWork;
        CloseButton.IsVisible = canWork
            || _assembly is { Status: MobilePickingAssemblyStatus.Closed }
                && _assembly.Sources.Any(x =>
                    x.ExchangeStage != MobileShippingExchangeStage.PostConfirmed);
        CloseButton.Text = _assembly?.Status == MobilePickingAssemblyStatus.Closed
            ? "Закрыть и отгрузить · повторить обмен" : "Закрыть и отгрузить";
        CloseButton.IsEnabled = !_busy && !pending;
        NavigationPage.SetHasBackButton(this, !_busy && !pending);
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        ProgressIndicator.IsRunning = busy && !SkuRefreshView.IsRefreshing;
        ProgressIndicator.IsVisible = ProgressIndicator.IsRunning;
        UpdateActions();
    }

    protected override bool OnBackButtonPressed()
    {
        if (!_busy && !_process.HasPendingCommand) return base.OnBackButtonPressed();
        ErrorLabel.Text = "Сначала завершите повтор команды.";
        return true;
    }
}

public sealed record PickingSkuPlanRow(Guid SkuId, string Name,
    decimal PlannedQuantity, decimal ConfirmedQuantity, string? Unit)
{
    public IReadOnlyList<PickingEntryListRow> Entries { get; init; } = [];
    public decimal? AvailableQuantity { get; init; }
    public PickingReplenishmentState ReplenishmentState { get; init; }
    public bool HasReplenishmentProposal { get; init; }
    public bool CanOrderMoreReplenishment => HasReplenishmentProposal
        && ReplenishmentState is (PickingReplenishmentState.Ordered or PickingReplenishmentState.InProgress);
    public bool HasReplenishmentIcon => ReplenishmentState != PickingReplenishmentState.Hidden;
    public bool ShowUnavailableSlash => ReplenishmentState == PickingReplenishmentState.Unavailable;
    public string ReplenishmentHint => ReplenishmentState switch
    {
        PickingReplenishmentState.CanOrder => "Заказать подпитку",
        PickingReplenishmentState.Ordered => "Подпитка заказана",
        PickingReplenishmentState.InProgress => "Подпитка выполняется",
        PickingReplenishmentState.Ready => "Готово: можно отобрать оставшийся план",
        PickingReplenishmentState.Unavailable => "Сейчас подпитать нельзя",
        _ => string.Empty
    };
    public Color ReplenishmentColor => ReplenishmentState switch
    {
        PickingReplenishmentState.CanOrder => Color.FromArgb("#1976D2"),
        PickingReplenishmentState.Ordered => Color.FromArgb("#E9B949"),
        PickingReplenishmentState.InProgress => Color.FromArgb("#E87924"),
        PickingReplenishmentState.Ready => Color.FromArgb("#358A4B"),
        _ => Color.FromArgb("#9E9E9E")
    };
    public string PlanText => $"План {PlannedQuantity:0.###} {Unit ?? "—"}";
    public string AvailableText => AvailableQuantity is decimal quantity
        ? $"Доступно {quantity:0.###}" : "Доступно —";
    public string FactText => $"Факт {ConfirmedQuantity:0.###}";
    public Color BackgroundColor => ConfirmedQuantity == PlannedQuantity
        ? Color.FromArgb("#E4F5E7")
        : ConfirmedQuantity > 0 ? Color.FromArgb("#FFF1CF") : Colors.White;
}

public enum PickingReplenishmentState
{
    Hidden, CanOrder, Ordered, InProgress, Ready, Unavailable
}

public sealed record PickingEntryListRow(Guid Id, string Address, decimal Quantity,
    DateOnly? ExpirationDate, bool CanReturn)
{
    public string QuantityText => Quantity.ToString("0.###", CultureInfo.CurrentCulture);
    public bool HasExpiration => ExpirationDate is not null;
    public string ExpirationText => ExpirationDate is DateOnly date ? $"до {date:dd.MM.yyyy}" : string.Empty;
    public string ReturnDescription => $"Вернуть {QuantityText} в ячейку {Address}"
        + (HasExpiration ? $", срок {ExpirationText}" : string.Empty);
}

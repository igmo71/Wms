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
    private bool _replenishmentsReady;

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
    public IReadOnlyList<PickingEntryListRow> EntryRows { get; private set; } = [];
    private bool IsOwner => _assembly?.ResponsibleUserId == _selection.CurrentUserId;

    public void Show(MobilePickingAssemblyResponse assembly, PickingAssemblyProcess process)
    {
        _process = process;
        Apply(assembly);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await RefreshAsync();
    }

    private async void OnRefreshing(object? sender, EventArgs e)
    {
        await RefreshAsync();
        SkuRefreshView.IsRefreshing = false;
    }

    private async Task RefreshAsync()
    {
        if (_busy || _assembly is null) return;
        SetBusy(true);
        try
        {
            var assembly = await _client.GetAsync(_assembly.Id, _assembly.WarehouseId);
            _work = await _client.WorkAsync(assembly.Id, assembly.WarehouseId);
            _replenishmentsReady = false;
            string? replenishmentError = null;
            if (assembly.Status == MobilePickingAssemblyStatus.InProgress
                && assembly.ResponsibleUserId == _selection.CurrentUserId)
            {
                try
                {
                    var list = await _replenishments.AssemblyAsync(assembly.Id,
                        assembly.WarehouseId);
                    var active = list.Tasks.Where(x =>
                        x.Status != MobileReplenishmentTaskStatus.Cancelled).ToArray();
                    _replenishmentsReady = active.Length > 0
                        && active.All(x => x.TransferredQuantity == x.OrderedQuantity)
                        && list.Proposals.Count == 0 && list.Deficits.Count == 0;
                }
                catch (MobileApiException exception)
                { replenishmentError = exception.Message; }
                catch (HttpRequestException)
                { replenishmentError = "Не удалось обновить Подпитки."; }
            }
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
        NumberLabel.Text = $"Сборка {assembly.Number}";
        StatusLabel.Text = assembly.Status switch
        {
            MobilePickingAssemblyStatus.Draft => "Черновик",
            MobilePickingAssemblyStatus.Preparing => "Ожидается перевод РО 1С в КОтбору",
            MobilePickingAssemblyStatus.InProgress => "Отбор в работе",
            MobilePickingAssemblyStatus.Closed => "Сборка закрыта",
            _ => "Сборка отменяется или отменена"
        };
        OwnerLabel.Text = $"Ответственный: {assembly.ResponsibleDisplayName}";
        SkuRows = _work?.Skus.Select(x => new PickingSkuPlanRow(
            x.SkuId, x.Name ?? x.Code ?? x.SkuId.ToString(),
            x.PlannedQuantity, x.ConfirmedQuantity, x.Unit)).ToArray()
            ?? assembly.PlanLines.GroupBy(x => x.SkuId)
                .Select(group => new PickingSkuPlanRow(group.Key,
                    group.First().SkuName ?? group.First().SkuCode ?? group.Key.ToString(),
                    group.Sum(x => x.Quantity), 0, null))
                .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(x => x.SkuId).ToArray();
        OnPropertyChanged(nameof(SkuRows));
        EntryRows = _work?.Entries.Select(x => new PickingEntryListRow(x.Id,
            $"{SkuRows.FirstOrDefault(sku => sku.SkuId == x.SkuId)?.Name ?? x.SkuId.ToString()} · "
            + $"{x.LocationCode} · {x.Quantity.ToString("0.###", CultureInfo.CurrentCulture)}"
            + (x.ExpirationDate is DateOnly date ? $" · до {date:dd.MM.yyyy}" : string.Empty),
            assembly.Status == MobilePickingAssemblyStatus.InProgress && IsOwner))
            .ToArray() ?? [];
        OnPropertyChanged(nameof(EntryRows));
        UpdateActions();
    }

    private async void OnSkuTapped(object? sender, TappedEventArgs e)
    {
        if (_busy || _process.HasPendingCommand || _assembly is null
            || _assembly.Status != MobilePickingAssemblyStatus.InProgress
            || !IsOwner || e.Parameter is not Guid skuId) return;
        var row = SkuRows.FirstOrDefault(x => x.SkuId == skuId);
        if (row is null || row.PlannedQuantity <= row.ConfirmedQuantity) return;
        var page = _services.GetRequiredService<PickingEntryPage>();
        page.ShowPick(_assembly, _process, skuId, null, false);
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
        var page = _services.GetRequiredService<PickingRoutePage>();
        page.Show(_assembly, _process);
        await Navigation.PushAsync(page);
    }

    private async void OnReplenishmentsClicked(object? sender, EventArgs e)
    {
        if (_busy || _process.HasPendingCommand || _assembly is null
            || _assembly.Status != MobilePickingAssemblyStatus.InProgress || !IsOwner) return;
        var page = _services.GetRequiredService<PickingReplenishmentsPage>();
        page.Show(_assembly, _process);
        await Navigation.PushAsync(page);
    }

    private async void OnCloseClicked(object? sender, EventArgs e)
    {
        if (_busy || _process.HasPendingCommand || _assembly is null
            || _assembly.Status != MobilePickingAssemblyStatus.InProgress || !IsOwner) return;
        SetBusy(true);
        try
        {
            var check = await _client.CheckClosureAsync(_assembly.Id, _assembly.WarehouseId);
            if (!check.CanOperatorClose)
            {
                ErrorLabel.Text = string.Join("\n", check.HardBlocks.Concat(check.ManagerIssues));
                if (string.IsNullOrWhiteSpace(ErrorLabel.Text))
                    ErrorLabel.Text = "Сборка требует решения руководителя склада.";
                return;
            }
            if (!await DisplayAlertAsync("Закрыть и отгрузить",
                "Складской факт будет проведён. Продолжить?", "Закрыть", "Отмена")) return;
            SetBusy(false);
            await RunCommandAsync(() => _process.CloseAsync(_assembly.Id,
                _assembly.WarehouseId, check.Revision));
        }
        catch (MobileApiException exception) { ErrorLabel.Text = exception.Message; }
        catch (HttpRequestException) { ErrorLabel.Text = "Не удалось сверить документы 1С."; }
        finally { SetBusy(false); }
    }

    private async void OnSourcesClicked(object? sender, EventArgs e)
    {
        if (_busy || _process.HasPendingCommand || _assembly is null) return;
        var page = _services.GetRequiredService<PickingSourcesPage>();
        page.Show(_assembly, _process);
        await Navigation.PushAsync(page);
        Navigation.RemovePage(this);
    }

    private async void OnStartClicked(object? sender, EventArgs e)
    {
        if (_busy || _process.HasPendingCommand || _assembly is null || !IsOwner) return;
        await RunCommandAsync(() => _process.StartAsync(
            _assembly.Id, _assembly.WarehouseId));
    }

    private async void OnTakeOverClicked(object? sender, EventArgs e)
    {
        if (_busy || _process.HasPendingCommand || _assembly is null || IsOwner) return;
        if (!await DisplayAlertAsync("Принять сборку",
            $"Сейчас за сборку отвечает {_assembly.ResponsibleDisplayName}. Принять её на себя?",
            "Принять", "Отмена")) return;
        await RunCommandAsync(() => _process.TakeOverAsync(
            _assembly.Id, _assembly.WarehouseId, _assembly.Revision));
    }

    private async void OnRetryClicked(object? sender, EventArgs e)
    {
        if (_busy || !_process.HasPendingCommand) return;
        await RunCommandAsync(_process.RetryAsync, retry: true);
    }

    private async Task RunCommandAsync(Func<Task<MobilePickingAssemblyResponse>> action,
        bool retry = false)
    {
        if (_busy || _process.HasPendingCommand && !retry) return;
        SetBusy(true);
        ErrorLabel.Text = string.Empty;
        try
        {
            var assembly = await action();
            _work = await _client.WorkAsync(assembly.Id, assembly.WarehouseId);
            Apply(assembly);
            if (_process.LastStartResult is { Status: MobilePickingAssemblyStatus.Preparing } start)
                ErrorLabel.Text = string.Join("\n", start.Sources
                    .Where(x => x.Error is not null).Select(x => x.Error));
        }
        catch (MobileApiException exception) { ErrorLabel.Text = exception.Message; }
        catch (HttpRequestException)
        {
            ErrorLabel.Text = _process.HasPendingCommand
                ? "Ответ не подтверждён. Повторите ту же команду."
                : "Действие выполнено, но экран не обновился. Потяните вниз.";
        }
        finally { SetBusy(false); }
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
        RouteButton.IsVisible = _assembly is { Status: MobilePickingAssemblyStatus.InProgress }
            && IsOwner;
        RouteButton.IsEnabled = !_busy && !pending;
        ReplenishmentsButton.IsVisible = RouteButton.IsVisible;
        ReplenishmentsButton.IsEnabled = !_busy && !pending;
        ReplenishmentsButton.Text = _replenishmentsReady ? "Подпитки ✓" : "Подпитки";
        CloseButton.IsVisible = RouteButton.IsVisible;
        CloseButton.IsEnabled = !_busy && !pending;
        NavigationPage.SetHasBackButton(this, !_busy && !pending);
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        ProgressIndicator.IsVisible = busy;
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
    public string PlanText => $"План {PlannedQuantity:0.###} {Unit ?? "—"}";
    public string FactText => $"Факт {ConfirmedQuantity:0.###}";
    public Color BackgroundColor => ConfirmedQuantity == PlannedQuantity
        ? Color.FromArgb("#E4F5E7")
        : ConfirmedQuantity > 0 ? Color.FromArgb("#FFF1CF") : Colors.White;
}

public sealed record PickingEntryListRow(Guid Id, string Text, bool CanReturn);

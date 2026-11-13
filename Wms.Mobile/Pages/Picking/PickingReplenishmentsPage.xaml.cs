using System.Globalization;
using Wms.Contracts.Mobile.V1;
using Wms.Mobile.Services;

namespace Wms.Mobile;

public partial class PickingReplenishmentsPage : ContentPage
{
    private readonly MobilePickingClient _picking;
    private readonly MobileReplenishmentClient _replenishments;
    private readonly MobileWarehouseSelection _selection;
    private readonly IServiceProvider _services;
    private PickingAssemblyProcess _process;
    private MobilePickingAssemblyResponse? _assembly;
    private bool _busy;

    public PickingReplenishmentsPage(MobilePickingClient picking,
        MobileReplenishmentClient replenishments, MobileWarehouseSelection selection,
        PickingAssemblyProcess process, IServiceProvider services)
    {
        InitializeComponent();
        _picking = picking;
        _replenishments = replenishments;
        _selection = selection;
        _services = services;
        _process = process;
        BindingContext = this;
    }

    public IReadOnlyList<PickingReplenishmentProposalRow> ProposalRows { get; private set; } = [];
    public IReadOnlyList<PickingReplenishmentTaskRow> TaskRows { get; private set; } = [];

    public void Show(MobilePickingAssemblyResponse assembly, PickingAssemblyProcess process)
    {
        _assembly = assembly;
        _process = process;
        NumberLabel.Text = $"Сборка {assembly.Number}";
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await RefreshAsync();
    }

    private async void OnRefreshing(object? sender, EventArgs e)
    {
        await RefreshAsync();
        ReplenishmentsRefreshView.IsRefreshing = false;
    }

    private async Task RefreshAsync()
    {
        if (_busy || _assembly is null) return;
        SetBusy(true);
        try
        {
            _assembly = await _picking.GetAsync(_assembly.Id, _assembly.WarehouseId);
            var list = await _replenishments.AssemblyAsync(_assembly.Id,
                _assembly.WarehouseId);
            NumberLabel.Text = $"Сборка {_assembly.Number}";
            var names = _assembly.PlanLines.GroupBy(x => x.SkuId)
                .ToDictionary(x => x.Key,
                    x => x.First().SkuName ?? x.First().SkuCode ?? x.Key.ToString());
            ProposalRows = list.Proposals.Select(x => new PickingReplenishmentProposalRow(
                x.SkuId, names.GetValueOrDefault(x.SkuId) ?? x.SkuId.ToString(),
                x.SourceLocationId, x.DestinationLocationId, x.Quantity,
                list.Revision,
                string.IsNullOrWhiteSpace(x.SourceName) ? x.SourceCode : x.SourceName,
                string.IsNullOrWhiteSpace(x.DestinationName) ? x.DestinationCode : x.DestinationName,
                string.Join(", ", x.Groups.Select(group =>
                    $"{(group.ExpirationDate is DateOnly date ? date.ToString("dd.MM.yyyy") : "без срока")}: "
                    + group.Quantity.ToString("0.###", CultureInfo.CurrentCulture)))))
                .ToArray();
            TaskRows = list.Tasks.Select(x => new PickingReplenishmentTaskRow(
                    names.GetValueOrDefault(x.SkuId) ?? x.SkuId.ToString(),
                    x.OrderedQuantity, x.TransferredQuantity, x.Status,
                    string.IsNullOrWhiteSpace(x.RecommendedSourceName)
                        ? x.RecommendedSourceCode : x.RecommendedSourceName,
                    string.IsNullOrWhiteSpace(x.RecommendedDestinationName)
                        ? x.RecommendedDestinationCode : x.RecommendedDestinationName))
                .ToArray();
            OnPropertyChanged(nameof(ProposalRows));
            OnPropertyChanged(nameof(TaskRows));
            NoProposalsLabel.IsVisible = ProposalRows.Count == 0;
            NoTasksLabel.IsVisible = TaskRows.Count == 0;
            DeficitLabel.Text = list.Deficits.Count == 0 ? string.Empty
                : "Нет доступного источника: " + string.Join("; ",
                    list.Deficits.Select(x =>
                        $"{names.GetValueOrDefault(x.SkuId) ?? x.SkuId.ToString()} — "
                        + x.Quantity.ToString("0.###", CultureInfo.CurrentCulture)));
            var activeTasks = TaskRows.Where(x =>
                x.Status != MobileReplenishmentTaskStatus.Cancelled).ToArray();
            ReadyLabel.Text = activeTasks.Length > 0
                && activeTasks.All(x => x.IsTransferred)
                && ProposalRows.Count == 0 && list.Deficits.Count == 0
                    ? "Все заказанные Подпитки проведены ✓"
                    : "Предложения и заказы обновляются при открытии и потягивании вниз.";
            if (!_process.HasPendingCommand) ErrorLabel.Text = string.Empty;
        }
        catch (MobileApiException exception) { ErrorLabel.Text = exception.Message; }
        catch (HttpRequestException) { ErrorLabel.Text = "Не удалось обновить Подпитки."; }
        finally { SetBusy(false); }
    }

    private async void OnOrderClicked(object? sender, EventArgs e)
    {
        if (_busy || _process.HasPendingCommand || _assembly is null
            || sender is not Button { CommandParameter: PickingReplenishmentProposalRow row })
            return;
        await RunOrderAsync(() => _process.OrderReplenishmentAsync(
            _assembly.Id, _assembly.WarehouseId, row.Revision,
            row.SkuId, row.SourceLocationId, row.DestinationLocationId,
            row.Quantity));
    }

    private async void OnRetryClicked(object? sender, EventArgs e)
    {
        if (_busy || !_process.HasPendingCommand) return;
        await RunOrderAsync(_process.RetryAsync, retry: true);
    }

    private async void OnSkuClicked(object? sender, EventArgs e) =>
        await SwitchSectionAsync(PickingSection.Sku);

    private async void OnRouteClicked(object? sender, EventArgs e) =>
        await SwitchSectionAsync(PickingSection.Route);

    private async void OnSourcesClicked(object? sender, EventArgs e) =>
        await SwitchSectionAsync(PickingSection.Sources);

    private async Task SwitchSectionAsync(PickingSection section)
    {
        if (_busy || _process.HasPendingCommand || _assembly is null) return;
        if (section == PickingSection.Route
            && (_assembly.Status != MobilePickingAssemblyStatus.InProgress
                || _assembly.ResponsibleUserId != _selection.CurrentUserId)) return;
        SetBusy(true);
        try
        {
            await PickingSectionNavigation.SwitchAsync(this, _services, _assembly,
                _process, section);
        }
        finally { SetBusy(false); }
    }

    private async Task RunOrderAsync(
        Func<Task<MobilePickingAssemblyResponse>> action, bool retry = false)
    {
        if (_busy || _process.HasPendingCommand && !retry) return;
        SetBusy(true);
        try
        {
            _assembly = await action();
            SetBusy(false);
            await RefreshAsync();
        }
        catch (MobileApiException exception) { ErrorLabel.Text = exception.Message; }
        catch (HttpRequestException)
        {
            ErrorLabel.Text = _process.HasPendingCommand
                ? "Ответ на заказ не подтверждён. Повторите ту же команду."
                : "Заказ сохранён, но обновление не удалось. Потяните вниз.";
        }
        finally { SetBusy(false); }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        ProgressIndicator.IsVisible = busy;
        RetryButton.IsVisible = !busy && _process.HasPendingCommand;
        if (RetryButton.IsVisible)
            RetryButton.Text = $"Повторить: {_process.PendingLabel}";
        var canNavigate = !busy && !_process.HasPendingCommand && _assembly is not null;
        SkuButton.IsEnabled = canNavigate;
        SourcesButton.IsEnabled = canNavigate;
        RouteButton.IsEnabled = canNavigate
            && _assembly?.Status == MobilePickingAssemblyStatus.InProgress
            && _assembly.ResponsibleUserId == _selection.CurrentUserId;
        NavigationPage.SetHasBackButton(this, !busy && !_process.HasPendingCommand);
    }

    protected override bool OnBackButtonPressed()
    {
        if (!_busy && !_process.HasPendingCommand) return base.OnBackButtonPressed();
        ErrorLabel.Text = "Сначала завершите повтор заказа.";
        return true;
    }
}

public sealed record PickingReplenishmentProposalRow(
    Guid SkuId, string SkuName, Guid SourceLocationId,
    Guid? DestinationLocationId, decimal Quantity, long Revision,
    string SourceAddress, string? DestinationAddress, string GroupsText)
{
    public string DetailText => $"{SourceAddress} → {DestinationAddress ?? "выбрать при переносе"} · "
        + Quantity.ToString("0.###", CultureInfo.CurrentCulture);
}

public sealed record PickingReplenishmentTaskRow(
    string SkuName, decimal OrderedQuantity, decimal TransferredQuantity,
    MobileReplenishmentTaskStatus Status,
    string? RecommendedSourceCode, string? RecommendedDestinationCode)
{
    public bool IsTransferred => TransferredQuantity == OrderedQuantity;
    public bool HasAddress => Status != MobileReplenishmentTaskStatus.Cancelled;
    public Color BackgroundColor => Status == MobileReplenishmentTaskStatus.Cancelled
        ? Color.FromArgb("#F0F0F0") : IsTransferred ? Color.FromArgb("#DDF2DD")
        : Status == MobileReplenishmentTaskStatus.InProgress
            ? Color.FromArgb("#FFF1C2") : Colors.White;
    public string ProgressText => Status == MobileReplenishmentTaskStatus.Cancelled
        ? $"Заказ {OrderedQuantity.ToString("0.###", CultureInfo.CurrentCulture)} отменён: потребность по SKU закрыта."
        : $"{TransferredQuantity.ToString("0.###", CultureInfo.CurrentCulture)}"
        + $" из {OrderedQuantity.ToString("0.###", CultureInfo.CurrentCulture)}"
        + (IsTransferred ? " · проведено" : Status == MobileReplenishmentTaskStatus.InProgress
            ? " · выполняется" : " · заказано");
    public string AddressText => $"Источник: {RecommendedSourceCode ?? "нет доступного источника"}"
        + $" · Назначение: {RecommendedDestinationCode ?? "выбрать при переносе"}";
}

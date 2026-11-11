using System.Globalization;
using Wms.Contracts.Mobile.V1;
using Wms.Mobile.Services;

namespace Wms.Mobile;

public partial class PickingReplenishmentsPage : ContentPage
{
    private readonly MobilePickingClient _picking;
    private readonly MobileReplenishmentClient _replenishments;
    private PickingAssemblyProcess _process;
    private MobilePickingAssemblyResponse? _assembly;
    private bool _busy;

    public PickingReplenishmentsPage(MobilePickingClient picking,
        MobileReplenishmentClient replenishments, PickingAssemblyProcess process)
    {
        InitializeComponent();
        _picking = picking;
        _replenishments = replenishments;
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
                list.Revision, x.SourceCode, x.DestinationCode,
                string.Join(", ", x.Groups.Select(group =>
                    $"{(group.ExpirationDate is DateOnly date ? date.ToString("dd.MM.yyyy") : "без срока")}: "
                    + group.Quantity.ToString("0.###", CultureInfo.CurrentCulture)))))
                .ToArray();
            TaskRows = list.Tasks.Where(x =>
                    x.Status != MobileReplenishmentTaskStatus.Cancelled)
                .Select(x => new PickingReplenishmentTaskRow(
                    names.GetValueOrDefault(x.SkuId) ?? x.SkuId.ToString(),
                    x.OrderedQuantity, x.TransferredQuantity, x.Status,
                    x.RecommendedSourceCode, x.RecommendedDestinationCode))
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
            ReadyLabel.Text = TaskRows.Count > 0
                && TaskRows.All(x => x.IsTransferred)
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
    string SourceCode, string? DestinationCode, string GroupsText)
{
    public string DetailText => $"{SourceCode} → {DestinationCode ?? "выбрать при переносе"} · "
        + Quantity.ToString("0.###", CultureInfo.CurrentCulture);
}

public sealed record PickingReplenishmentTaskRow(
    string SkuName, decimal OrderedQuantity, decimal TransferredQuantity,
    MobileReplenishmentTaskStatus Status,
    string? RecommendedSourceCode, string? RecommendedDestinationCode)
{
    public bool IsTransferred => TransferredQuantity == OrderedQuantity;
    public Color BackgroundColor => IsTransferred ? Color.FromArgb("#DDF2DD")
        : Status == MobileReplenishmentTaskStatus.InProgress
            ? Color.FromArgb("#FFF1C2") : Colors.White;
    public string ProgressText => $"{TransferredQuantity.ToString("0.###", CultureInfo.CurrentCulture)}"
        + $" из {OrderedQuantity.ToString("0.###", CultureInfo.CurrentCulture)}"
        + (IsTransferred ? " · проведено" : Status == MobileReplenishmentTaskStatus.InProgress
            ? " · выполняется" : " · заказано");
    public string AddressText => $"Источник: {RecommendedSourceCode ?? "нет доступного источника"}"
        + $" · Назначение: {RecommendedDestinationCode ?? "выбрать при переносе"}";
}

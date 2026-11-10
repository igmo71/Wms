using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Wms.Contracts.Mobile.V1;
using Wms.Mobile.Services;

namespace Wms.Mobile;

public partial class PickingSkuPage : ContentPage
{
    private readonly MobilePickingClient _client;
    private readonly MobileWarehouseSelection _selection;
    private readonly IServiceProvider _services;
    private PickingAssemblyProcess _process;
    private MobilePickingAssemblyResponse? _assembly;
    private bool _busy;
    private bool _useSuppliedAssembly;

    public PickingSkuPage(MobilePickingClient client,
        MobileWarehouseSelection selection, PickingAssemblyProcess process,
        IServiceProvider services)
    {
        InitializeComponent();
        _client = client;
        _selection = selection;
        _process = process;
        _services = services;
        BindingContext = this;
    }

    public IReadOnlyList<PickingSkuPlanRow> SkuRows { get; private set; } = [];
    private bool IsOwner => _assembly?.ResponsibleUserId == _selection.CurrentUserId;

    public void Show(MobilePickingAssemblyResponse assembly, PickingAssemblyProcess process)
    {
        _process = process;
        _useSuppliedAssembly = true;
        Apply(assembly);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_useSuppliedAssembly)
            _useSuppliedAssembly = false;
        else await RefreshAsync();
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
            Apply(await _client.GetAsync(_assembly.Id, _assembly.WarehouseId));
            if (!_process.HasPendingCommand) ErrorLabel.Text = string.Empty;
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
        SkuRows = assembly.PlanLines.GroupBy(x => x.SkuId)
            .Select(group => new PickingSkuPlanRow(group.Key,
                group.First().SkuName ?? group.First().SkuCode ?? group.Key.ToString(),
                group.Sum(x => x.Quantity).ToString("0.###", CultureInfo.CurrentCulture)))
            .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(x => x.SkuId).ToArray();
        OnPropertyChanged(nameof(SkuRows));
        UpdateActions();
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
            Apply(await action());
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

public sealed record PickingSkuPlanRow(Guid SkuId, string Name, string QuantityText);

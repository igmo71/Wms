using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Wms.Contracts.Mobile.V1;
using Wms.Mobile.Services;

namespace Wms.Mobile;

public partial class PickingRoutePage : ContentPage
{
    private readonly MobilePickingClient _client;
    private readonly IServiceProvider _services;
    private PickingAssemblyProcess _process;
    private MobilePickingAssemblyResponse? _assembly;
    private bool _busy;

    public PickingRoutePage(MobilePickingClient client,
        PickingAssemblyProcess process, IServiceProvider services)
    {
        InitializeComponent();
        _client = client;
        _process = process;
        _services = services;
        BindingContext = this;
    }

    public IReadOnlyList<PickingRouteRow> Steps { get; private set; } = [];
    public IReadOnlyList<PickingRouteGapRow> Gaps { get; private set; } = [];

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
        RouteRefreshView.IsRefreshing = false;
    }

    private async Task RefreshAsync()
    {
        if (_busy || _assembly is null) return;
        SetBusy(true);
        try
        {
            var assembly = await _client.GetAsync(_assembly.Id, _assembly.WarehouseId);
            var route = await _client.RouteAsync(assembly.Id, assembly.WarehouseId);
            _assembly = assembly;
            var work = await _client.WorkAsync(assembly.Id, assembly.WarehouseId);
            var names = work.Skus.ToDictionary(x => x.SkuId,
                x => (x.Name ?? x.Code ?? x.SkuId.ToString(), x.Unit));
            Steps = route.Steps.Select((x, index) =>
            {
                var (name, unit) = names.GetValueOrDefault(x.SkuId);
                return new PickingRouteRow(x.SkuId, x.LocationCode,
                    x.DirectPalletPick,
                    $"{index + 1}. {x.LocationCode} · {name ?? x.SkuId.ToString()}",
                    $"{x.Quantity.ToString("0.###", CultureInfo.CurrentCulture)} {unit ?? "—"}"
                    + (x.ExpirationDate is DateOnly date ? $" · до {date:dd.MM.yyyy}" : "")
                    + (x.DirectPalletPick ? " · снять весь поддон" : ""));
            }).ToArray();
            Gaps = route.Gaps.Select(x => new PickingRouteGapRow(
                $"{names.GetValueOrDefault(x.SkuId).Item1 ?? x.SkuId.ToString()}: "
                + $"{x.Quantity.ToString("0.###", CultureInfo.CurrentCulture)} — нужна Подпитка или разбор"
            )).ToArray();
            OnPropertyChanged(nameof(Steps));
            OnPropertyChanged(nameof(Gaps));
            EmptyLabel.IsVisible = Steps.Count == 0;
            ErrorLabel.Text = string.Empty;
        }
        catch (MobileApiException exception) { ErrorLabel.Text = exception.Message; }
        catch (HttpRequestException) { ErrorLabel.Text = "Не удалось обновить маршрут."; }
        finally { SetBusy(false); }
    }

    private async void OnStepTapped(object? sender, TappedEventArgs e)
    {
        if (_busy || _process.HasPendingCommand || _assembly is null
            || e.Parameter is not PickingRouteRow row) return;
        var page = _services.GetRequiredService<PickingEntryPage>();
        page.ShowPick(_assembly, _process, row.SkuId,
            row.LocationCode, row.DirectPalletPick);
        await Navigation.PushAsync(page);
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
        finally { SetBusy(false); }
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
            await Navigation.PopAsync();
        }
        catch (MobileApiException exception) { ErrorLabel.Text = exception.Message; }
        catch (HttpRequestException)
        {
            ErrorLabel.Text = _process.HasPendingCommand
                ? "Ответ не подтверждён. Повторите ту же команду."
                : "Сборка закрыта, но экран не обновился. Вернитесь к SKU.";
        }
        finally { SetBusy(false); }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        ProgressIndicator.IsVisible = busy;
        RetryButton.IsVisible = !busy && _process.HasPendingCommand;
        CloseButton.IsEnabled = !busy && !_process.HasPendingCommand;
        NavigationPage.SetHasBackButton(this, !busy && !_process.HasPendingCommand);
    }

    protected override bool OnBackButtonPressed()
    {
        if (!_busy && !_process.HasPendingCommand) return base.OnBackButtonPressed();
        ErrorLabel.Text = "Сначала завершите текущую команду.";
        return true;
    }
}

public sealed record PickingRouteRow(Guid SkuId, string LocationCode,
    bool DirectPalletPick, string Heading, string Detail);

public sealed record PickingRouteGapRow(string Text);

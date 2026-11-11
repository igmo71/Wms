using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Wms.Contracts.Mobile.V1;
using Wms.Mobile.Services;

namespace Wms.Mobile;

public partial class ReplenishmentQueuePage : ContentPage
{
    private readonly MobileReplenishmentClient _client;
    private readonly MobileReferenceDataClient _references;
    private readonly MobileWarehouseSelection _selection;
    private readonly IServiceProvider _services;
    private bool _loaded;
    private bool _busy;

    public ReplenishmentQueuePage(MobileReplenishmentClient client,
        MobileReferenceDataClient references, MobileWarehouseSelection selection,
        IServiceProvider services)
    {
        InitializeComponent();
        _client = client;
        _references = references;
        _selection = selection;
        _services = services;
        BindingContext = this;
    }

    public IReadOnlyList<ReplenishmentQueueRow> TaskRows { get; private set; } = [];
    private MobileWarehouseResponse? Warehouse =>
        WarehousePicker.SelectedItem as MobileWarehouseResponse;

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (!_loaded)
        {
            try
            {
                var warehouses = await _references.GetWarehousesAsync();
                WarehousePicker.ItemsSource = warehouses.ToList();
                var (index, warning) = _selection.Resolve(warehouses);
                if (index >= 0) WarehousePicker.SelectedIndex = index;
                ErrorLabel.Text = warning ?? string.Empty;
                _loaded = true;
            }
            catch (MobileApiException exception) { ErrorLabel.Text = exception.Message; }
            catch (HttpRequestException) { ErrorLabel.Text = "Сервер WMS недоступен."; }
        }
        await RefreshAsync();
    }

    private async void OnWarehouseChanged(object? sender, EventArgs e)
    {
        if (Warehouse is { } warehouse) _selection.Select(warehouse.Id);
        await RefreshAsync();
    }

    private async void OnRefreshing(object? sender, EventArgs e)
    {
        await RefreshAsync();
        QueueRefreshView.IsRefreshing = false;
    }

    private async Task RefreshAsync()
    {
        if (_busy || Warehouse is not { } warehouse) return;
        SetBusy(true);
        try
        {
            var tasks = await _client.QueueAsync(warehouse.Id);
            TaskRows = tasks.Select(x => new ReplenishmentQueueRow(x.Id,
                $"Сборка {x.AssemblyNumber} · {x.SkuName ?? x.SkuCode ?? x.SkuId.ToString()}",
                x.PriorityQueue, x.OrderedQuantity, x.TransferredQuantity,
                x.Status, x.AssignedTo, x.RecommendedSourceCode,
                x.RecommendedDestinationCode)).ToArray();
            OnPropertyChanged(nameof(TaskRows));
            EmptyLabel.IsVisible = TaskRows.Count == 0;
            ErrorLabel.Text = string.Empty;
        }
        catch (MobileApiException exception) { ErrorLabel.Text = exception.Message; }
        catch (HttpRequestException) { ErrorLabel.Text = "Не удалось обновить очередь Подпитки."; }
        finally
        {
            SetBusy(false);
            if (Warehouse is { } current && current.Id != warehouse.Id)
                await RefreshAsync();
        }
    }

    private async void OnTaskTapped(object? sender, TappedEventArgs e)
    {
        if (_busy || Warehouse is not { } warehouse || e.Parameter is not Guid id) return;
        var page = _services.GetRequiredService<ReplenishmentTaskPage>();
        page.Show(id, warehouse.Id);
        await Navigation.PushAsync(page);
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        ProgressIndicator.IsVisible = busy;
        WarehousePicker.IsEnabled = !busy;
    }
}

public sealed record ReplenishmentQueueRow(Guid Id, string Title,
    string Priority, decimal Ordered, decimal Transferred,
    MobileReplenishmentTaskStatus Status, string? AssignedTo,
    string? SourceCode, string? DestinationCode)
{
    public string PriorityText => "Приоритет: " + (Priority switch
    {
        "LiveQueue" => "живая очередь",
        "CollectByDate" => "к дате",
        "OwnDelivery" => "самовывоз",
        _ => "не указан"
    });
    public string ProgressText => $"Проведено {Transferred.ToString("0.###", CultureInfo.CurrentCulture)}"
        + $" из {Ordered.ToString("0.###", CultureInfo.CurrentCulture)}";
    public string AddressText => $"Источник: {SourceCode ?? "выбрать по скану"}"
        + $" · Назначение: {DestinationCode ?? "выбрать по скану"}";
    public string StateText => Status == MobileReplenishmentTaskStatus.InProgress
        ? $"В работе · исполнитель: {AssignedTo}" : "Ожидает начала";
}

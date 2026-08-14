using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Options;
using MudBlazor;
using Wms.Application.Services.ReceivingOrders;
using Wms.Application.Services;
using Wms.Common;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.WebApp.Components.Pages.ReceivingOrderPages;

public partial class Index : IAsyncDisposable
{
    [Inject] private ReceivingOrderQueryService OrderQueryService { get; set; } = null!;
    [Inject] private WarehouseService WarehouseService { get; set; } = null!;

    [Inject] private IOptions<WmsSettings> Options { get; set; } = null!;

    private MudDataGrid<ReceivingOrder> _dataGrid = null!;
    private string? _searchString;
    private DateTime? _dateFrom;
    private DateTime? _dateTo;
    private ReceivingOrderStatus? _status;
    private ReceivingOrderQueue? _queue;
    private WarehouseOperation? _warehouseOperation;
    private Warehouse? _warehouse;
    private WmsSettings? _wmsSettings;
    private readonly CancellationTokenSource _refreshCts = new();

    private static string GetOrderHref(ReceivingOrder order) => order.Status is ReceivingOrderStatus.InReceiving
        or ReceivingOrderStatus.ProcessingRequired
            ? $"receiving-orders/{order.Id}/in-process"
            : $"receiving-orders/{order.Id}";

    private async Task<GridData<ReceivingOrder>> LoadServerDataAsync(GridState<ReceivingOrder> state, CancellationToken cancellationToken)
    {
        var sortDefinition = state.SortDefinitions.FirstOrDefault();

        var result = await OrderQueryService.ListOrdersAsync(new ReceivingOrderListQuery
        {
            SearchString = _searchString,
            DateFrom = _dateFrom,
            DateTo = _dateTo,
            WarehouseId = _warehouse?.Id,
            Status = _status,
            Queue = _queue,
            WarehouseOperation = _warehouseOperation,
            SortBy = sortDefinition?.SortBy,
            SortDescending = sortDefinition?.Descending ?? true,
            Skip = state.Page * state.PageSize,
            Take = state.PageSize
        }, cancellationToken);

        return new GridData<ReceivingOrder>
        {
            Items = result.Items,
            TotalItems = result.TotalItems
        };
    }

    private Task OnSearchChangedAsync(string searchString)
    {
        _searchString = searchString;
        return _dataGrid.ReloadServerData();
    }

    private Task OnDateFromChangedAsync(DateTime? dateFrom)
    {
        _dateFrom = dateFrom;
        return _dataGrid.ReloadServerData();
    }

    private Task OnDateToChangedAsync(DateTime? dateTo)
    {
        _dateTo = dateTo;
        return _dataGrid.ReloadServerData();
    }

    private Task OnStatusChangedAsync(ReceivingOrderStatus? status)
    {
        _status = status;
        return _dataGrid.ReloadServerData();
    }

    private Task OnQueueChangedAsync(ReceivingOrderQueue? queue)
    {
        _queue = queue;
        return _dataGrid.ReloadServerData();
    }

    private async Task<IEnumerable<Warehouse>> SearchWarehousesAsync(string? searchText, CancellationToken ct)
    {
        var result = await WarehouseService.ListAsync(new ListQuery
        {
            SearchString = searchText,
            SortBy = "Name",
            Take = 10
        }, ct);

        return result.Items;
    }

    private Task OnWarehouseChangedAsync(Warehouse? warehouse)
    {
        _warehouse = warehouse;
        return _dataGrid.ReloadServerData();
    }

    private Task OnWarehouseOperationChangedAsync(WarehouseOperation? warehouseOperation)
    {
        _warehouseOperation = warehouseOperation;
        return _dataGrid.ReloadServerData();
    }

    protected override Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            _wmsSettings = Options.Value;
            _ = RefreshLoopAsync(_refreshCts.Token);
        }

        return Task.CompletedTask;
    }

    private async Task RefreshLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_wmsSettings?.OrdersRefreshLoop ?? 5));

        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                await InvokeAsync(async () =>
                {
                    if (_dataGrid is not null)
                        await _dataGrid.ReloadServerData();
                });
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
    }

    public ValueTask DisposeAsync()
    {
        _refreshCts.Cancel();
        _refreshCts.Dispose();

        return ValueTask.CompletedTask;
    }
}

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Options;
using MudBlazor;
using Wms.Application.Services;
using Wms.Common;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.WebApp.Components.Pages.ShippingOrderPages;

public partial class Index : IAsyncDisposable
{
    [Inject] private ShippingOrderQueryService OrderQueryService { get; set; } = null!;

    [Inject] private IOptions<WmsSettings> Options { get; set; } = null!;

    private MudDataGrid<ShippingOrder> _dataGrid = null!;
    private string? _searchString;
    private DateTime? _dateFrom;
    private DateTime? _dateTo;
    private ShippingOrderStatus? _status;
    private ShippingOrderQueue? _queue;
    private WmsSettings? _wmsSettings;
    private readonly CancellationTokenSource _refreshCts = new();

    private static string GetOrderHref(ShippingOrder order) => order.Status is ShippingOrderStatus.ReadyForPicking
        or ShippingOrderStatus.ReadyForVerification
        or ShippingOrderStatus.InVerification
        or ShippingOrderStatus.Verified
            ? $"shipping-orders/{order.Id}/picking"
            : $"shipping-orders/{order.Id}";

    private async Task<GridData<ShippingOrder>> LoadServerDataAsync(GridState<ShippingOrder> state, CancellationToken cancellationToken)
    {
        var sortDefinition = state.SortDefinitions.FirstOrDefault();

        var result = await OrderQueryService.ListOrdersAsync(new ShippingOrderListQuery
        {
            SearchString = _searchString,
            DateFrom = _dateFrom,
            DateTo = _dateTo,
            Status = _status,
            Queue = _queue,
            SortBy = sortDefinition?.SortBy,
            SortDescending = sortDefinition?.Descending ?? true,
            Skip = state.Page * state.PageSize,
            Take = state.PageSize
        }, cancellationToken);

        return new GridData<ShippingOrder>
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

    private Task OnStatusChangedAsync(ShippingOrderStatus? status)
    {
        _status = status;
        return _dataGrid.ReloadServerData();
    }

    private Task OnQueueChangedAsync(ShippingOrderQueue? queue)
    {
        _queue = queue;
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

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Options;
using MudBlazor;
using Wms.Application.Services.ReceivingOrders;
using Wms.Common;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.WebApp.Components.Pages.ReceivingOrderPages;

public partial class Index : IAsyncDisposable
{
    [Inject]
    private ReceivingOrderQueryService OrderQueryService { get; set; } = null!;

    [Inject]
    private ReceivingOrderCommandService OrderCommandService { get; set; } = null!;

    [Inject]
    private IOptions<WmsSettings> Options { get; set; } = null!;

    private MudDataGrid<ReceivingOrder> _dataGrid = null!;
    private bool _dataGridLoading = true;
    private string? _searchString;
    private DateTime? _dateFrom;
    private DateTime? _dateTo;
    private ReceivingOrderStatus? _status;
    private WmsSettings? _wmsSettings;
    private readonly CancellationTokenSource _refreshCts = new();

    private async Task<GridData<ReceivingOrder>> LoadServerDataAsync(
        GridState<ReceivingOrder> state,
        CancellationToken cancellationToken)
    {
        var sortDefinition = state.SortDefinitions.FirstOrDefault();
        var query = new DocumentListQuery
        {
            SearchString = _searchString,
            DateFrom = _dateFrom,
            DateTo = _dateTo,
            Status = _status,
            SortBy = sortDefinition?.SortBy,
            SortDescending = sortDefinition?.Descending ?? true,
            Skip = state.Page * state.PageSize,
            Take = state.PageSize
        };

        try
        {
            var result = await OrderQueryService.ListOrdersAsync(query, cancellationToken);

            return new GridData<ReceivingOrder>
            {
                Items = result.Items,
                TotalItems = result.TotalItems
            };
        }
        finally
        {
            _dataGridLoading = false;
        }
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
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_wmsSettings?.ReceivingRefreshLoop ?? 5));

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

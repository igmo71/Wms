using Microsoft.AspNetCore.Components;
using MudBlazor;
using Wms.Application;
using Wms.Common;
using Wms.Domain;

namespace Wms.WebApp.Components.ReceivingOrderPages;

public partial class Index
{
    [Inject]
    private ReceivingOrderService ReceivingOrderService { get; set; } = null!;

    private MudDataGrid<ReceivingOrder> _dataGrid = null!;
    private string? _searchString;
    private DateTime? _dateFrom;
    private DateTime? _dateTo;

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
            SortBy = sortDefinition?.SortBy,
            SortDescending = sortDefinition?.Descending ?? true,
            Skip = state.Page * state.PageSize,
            Take = state.PageSize
        };

        var result = await ReceivingOrderService.ListAsync(query, cancellationToken);

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
}

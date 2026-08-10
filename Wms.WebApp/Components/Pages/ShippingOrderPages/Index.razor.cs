using Microsoft.AspNetCore.Components;
using MudBlazor;
using Wms.Application.Services;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.WebApp.Components.Pages.ShippingOrderPages;

public partial class Index
{
    [Inject] private ShippingOrderQueryService OrderQueryService { get; set; } = null!;

    private MudDataGrid<ShippingOrder> _dataGrid = null!;
    private bool _dataGridLoading = true;
    private string? _searchString;
    private DateTime? _dateFrom;
    private DateTime? _dateTo;
    private ShippingOrderStatus? _status;

    private async Task<GridData<ShippingOrder>> LoadServerDataAsync(GridState<ShippingOrder> state, CancellationToken cancellationToken)
    {
        var sortDefinition = state.SortDefinitions.FirstOrDefault();
        var result = await OrderQueryService.ListOrdersAsync(new ShippingOrderListQuery
        {
            SearchString = _searchString,
            DateFrom = _dateFrom,
            DateTo = _dateTo,
            Status = _status,
            SortBy = sortDefinition?.SortBy,
            SortDescending = sortDefinition?.Descending ?? true,
            Skip = state.Page * state.PageSize,
            Take = state.PageSize
        }, cancellationToken);

        _dataGridLoading = false;
        return new GridData<ShippingOrder> { Items = result.Items, TotalItems = result.TotalItems };
    }

    private Task OnSearchChangedAsync(string searchString) { _searchString = searchString; return _dataGrid.ReloadServerData(); }
    private Task OnDateFromChangedAsync(DateTime? dateFrom) { _dateFrom = dateFrom; return _dataGrid.ReloadServerData(); }
    private Task OnDateToChangedAsync(DateTime? dateTo) { _dateTo = dateTo; return _dataGrid.ReloadServerData(); }
    private Task OnStatusChangedAsync(ShippingOrderStatus? status) { _status = status; return _dataGrid.ReloadServerData(); }
}

using Microsoft.AspNetCore.Components;
using MudBlazor;
using Wms.Application.Reports;
using Wms.Application.Warehouses;
using Wms.Common;
using Wms.Domain;

namespace Wms.WebApp.Components.Pages.ReportPages.UndatedExpirationStock;

public partial class Index
{
    [Inject] private GetUndatedExpirationStock Report { get; set; } = null!;
    [Inject] private WarehouseService Warehouses { get; set; } = null!;

    private MudDataGrid<UndatedExpirationStockRow> _dataGrid = null!;
    private Warehouse? _warehouse;

    private async Task<GridData<UndatedExpirationStockRow>> LoadServerDataAsync(
        GridState<UndatedExpirationStockRow> state, CancellationToken ct)
    {
        var page = await Report.ExecuteAsync(
            _warehouse?.Id, state.Page * state.PageSize, state.PageSize, ct);
        return new GridData<UndatedExpirationStockRow>
        {
            Items = page.Items,
            TotalItems = page.TotalCount
        };
    }

    private async Task<IEnumerable<Warehouse>> SearchWarehousesAsync(
        string? searchText, CancellationToken ct)
    {
        var result = await Warehouses.ListAsync(new ListQuery
        {
            SearchString = searchText,
            SortBy = "Name",
            ExcludeDeleted = false,
            Take = 10
        }, ct);
        return result.Items;
    }

    private Task OnWarehouseChangedAsync(Warehouse? warehouse)
    {
        _warehouse = warehouse;
        return _dataGrid.ReloadServerData();
    }
}

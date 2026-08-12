using Microsoft.AspNetCore.Components;
using MudBlazor;
using Wms.Application.Services;
using Wms.Common;
using Wms.Domain;

namespace Wms.WebApp.Components.Pages.StockKeepingUnitPages;

public partial class Index
{
    [Inject] private StockKeepingUnitService StockKeepingUnitService { get; set; } = null!;
    [Inject] private SynchronizedCatalogImportService SynchronizedCatalogImportService { get; set; } = null!;
    private MudDataGrid<StockKeepingUnit> _dataGrid = null!;
    private string? _searchString;
    private bool _includeDeleted;
    private bool _isImporting;
    private bool _importFailed;

    private async Task<GridData<StockKeepingUnit>> LoadServerDataAsync(GridState<StockKeepingUnit> state, CancellationToken ct)
    {
        var sort = state.SortDefinitions.FirstOrDefault();
        var result = await StockKeepingUnitService.ListAsync(new ListQuery { SearchString = _searchString, ExcludeDeleted = !_includeDeleted, SortBy = sort?.SortBy, SortDescending = sort?.Descending ?? false, Skip = state.Page * state.PageSize, Take = state.PageSize }, ct);
        return new GridData<StockKeepingUnit> { Items = result.Items, TotalItems = result.TotalItems };
    }

    private Task OnSearchChangedAsync(string? value) { _searchString = value; return _dataGrid.ReloadServerData(); }
    private Task OnIncludeDeletedChangedAsync(bool value) { _includeDeleted = value; return _dataGrid.ReloadServerData(); }

    private async Task RefreshFromOneCAsync()
    {
        _isImporting = true;
        _importFailed = false;
        try
        {
            await SynchronizedCatalogImportService.RefreshStockKeepingUnitsAsync();
            await _dataGrid.ReloadServerData();
        }
        catch
        {
            _importFailed = true;
        }
        finally
        {
            _isImporting = false;
        }
    }
}

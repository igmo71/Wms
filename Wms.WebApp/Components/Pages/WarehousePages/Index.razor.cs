using Microsoft.AspNetCore.Components;
using MudBlazor;
using Wms.Application;
using Wms.Common;
using Wms.Domain;

namespace Wms.WebApp.Components.Pages.WarehousePages;

public partial class Index
{
    [Inject]
    private WarehouseService WarehouseService { get; set; } = null!;

    [Inject]
    private WarehouseImportService WarehouseImportService { get; set; } = null!;

    private MudDataGrid<Warehouse> _dataGrid = null!;
    private string? _searchString;
    private bool _includeDeleted;
    private bool _isImporting;
    private bool _importFailed;

    private async Task<GridData<Warehouse>> LoadServerDataAsync(
        GridState<Warehouse> state,
        CancellationToken cancellationToken)
    {
        var sortDefinition = state.SortDefinitions.FirstOrDefault();
        var query = new ListQuery
        {
            SearchString = _searchString,
            ExcludeDeleted = !_includeDeleted,
            SortBy = sortDefinition?.SortBy,
            SortDescending = sortDefinition?.Descending ?? false,
            Skip = state.Page * state.PageSize,
            Take = state.PageSize
        };

        var result = await WarehouseService.ListAsync(query, cancellationToken);

        return new GridData<Warehouse>
        {
            Items = result.Items,
            TotalItems = result.TotalItems
        };
    }

    private Task OnSearchChangedAsync(string? searchString)
    {
        _searchString = searchString;
        return _dataGrid.ReloadServerData();
    }

    private Task OnIncludeDeletedChangedAsync(bool includeDeleted)
    {
        _includeDeleted = includeDeleted;
        return _dataGrid.ReloadServerData();
    }

    private async Task RefreshFromOneCAsync()
    {
        _isImporting = true;
        _importFailed = false;

        try
        {
            await WarehouseImportService.RefreshFromOneCAsync();
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

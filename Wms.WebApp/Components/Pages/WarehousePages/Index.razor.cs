using Microsoft.AspNetCore.Components;
using MudBlazor;
using Wms.Application.CatalogSynchronization;
using Wms.Application.Warehouses;
using Wms.Common;
using Wms.Domain;

namespace Wms.WebApp.Components.Pages.WarehousePages;

public partial class Index
{
    [Inject]
    private WarehouseService WarehouseService { get; set; } = null!;

    [Inject]
    private SynchronizedCatalogImportService SynchronizedCatalogImportService { get; set; } = null!;

    private MudDataGrid<Warehouse> _dataGrid = null!;
    private string? _searchString;
    private bool _includeDeleted;
    private bool _isImporting;
    private bool _importFailed;
    private bool _importSucceeded;
    private string? _importMessage;

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
        _importSucceeded = false;
        _importMessage = null;

        try
        {
            var result = await SynchronizedCatalogImportService.RefreshWarehousesAsync();
            if (!result.IsSuccess)
            {
                _importFailed = true;
                _importMessage = result.Error?.Message ?? "Не удалось обновить склады из 1С.";
                return;
            }

            await _dataGrid.ReloadServerData();
            _importSucceeded = true;
            _importMessage = "Склады успешно обновлены из 1С.";
        }
        catch
        {
            _importFailed = true;
            _importMessage = "Не удалось обновить склады из 1С.";
        }
        finally
        {
            _isImporting = false;
        }
    }
}

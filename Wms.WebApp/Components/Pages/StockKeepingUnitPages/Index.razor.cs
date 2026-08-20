using Microsoft.AspNetCore.Components;
using MudBlazor;
using Wms.Application.StockKeepingUnits;
using Wms.Common;
using Wms.Domain;
using Wms.Integration.OneS.Services;

namespace Wms.WebApp.Components.Pages.StockKeepingUnitPages;

public partial class Index
{
    [Inject] private StockKeepingUnitService StockKeepingUnitService { get; set; } = null!;
    [Inject] private Catalog_Номенклатура_Service CatalogImportService { get; set; } = null!;
    private MudDataGrid<StockKeepingUnit> _dataGrid = null!;
    private string? _searchString;
    private bool _includeDeleted;
    private bool _isImporting;
    private bool _importFailed;
    private bool _importSucceeded;
    private string? _importMessage;

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
        _importSucceeded = false;
        _importMessage = null;
        try
        {
            var result = await CatalogImportService.ImportListAsync();
            if (!result.IsSuccess)
            {
                _importFailed = true;
                _importMessage = result.Error?.Message ?? "Не удалось обновить номенклатуру из 1С.";
                return;
            }

            await _dataGrid.ReloadServerData();
            _importSucceeded = true;
            _importMessage = "Номенклатура успешно обновлена из 1С.";
        }
        catch
        {
            _importFailed = true;
            _importMessage = "Не удалось обновить номенклатуру из 1С.";
        }
        finally
        {
            _isImporting = false;
        }
    }
}

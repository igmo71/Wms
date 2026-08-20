using Microsoft.AspNetCore.Components;
using MudBlazor;
using Wms.Application.CatalogSynchronization;
using Wms.Application.UnitsOfMeasure;
using Wms.Common;
using Wms.Domain;

namespace Wms.WebApp.Components.Pages.UnitOfMeasurePages;

public partial class Index
{
    [Inject] private UnitOfMeasureService UnitOfMeasureService { get; set; } = null!;
    [Inject] private SynchronizedCatalogImportService SynchronizedCatalogImportService { get; set; } = null!;
    private MudDataGrid<UnitOfMeasure> _dataGrid = null!;
    private string? _searchString;
    private bool _includeDeleted;
    private bool _isImporting;
    private bool _importFailed;
    private bool _importSucceeded;
    private string? _importMessage;
    private async Task<GridData<UnitOfMeasure>> LoadServerDataAsync(GridState<UnitOfMeasure> state, CancellationToken ct)
    {
        var sort = state.SortDefinitions.FirstOrDefault();
        var result = await UnitOfMeasureService.ListAsync(new ListQuery { SearchString = _searchString, ExcludeDeleted = !_includeDeleted, SortBy = sort?.SortBy, SortDescending = sort?.Descending ?? false, Skip = state.Page * state.PageSize, Take = state.PageSize }, ct);
        return new GridData<UnitOfMeasure> { Items = result.Items, TotalItems = result.TotalItems };
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
            var result = await SynchronizedCatalogImportService.RefreshUnitsOfMeasureAsync();
            if (!result.IsSuccess)
            {
                _importFailed = true;
                _importMessage = result.Error?.Message ?? "Не удалось обновить единицы измерения из 1С.";
                return;
            }

            await _dataGrid.ReloadServerData();
            _importSucceeded = true;
            _importMessage = "Единицы измерения успешно обновлены из 1С.";
        }
        catch
        {
            _importFailed = true;
            _importMessage = "Не удалось обновить единицы измерения из 1С.";
        }
        finally
        {
            _isImporting = false;
        }
    }
}

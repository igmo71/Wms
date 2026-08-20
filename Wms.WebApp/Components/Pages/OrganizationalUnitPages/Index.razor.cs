using Microsoft.AspNetCore.Components;
using MudBlazor;
using Wms.Application.OrganizationalUnits;
using Wms.Common;
using Wms.Domain;
using Wms.Integration.OneS.Services;

namespace Wms.WebApp.Components.Pages.OrganizationalUnitPages;

public partial class Index
{
    [Inject] private OrganizationalUnitService OrganizationalUnitService { get; set; } = null!;
    [Inject] private Catalog_СтруктураПредприятия_Service CatalogImportService { get; set; } = null!;

    private MudDataGrid<OrganizationalUnit> _dataGrid = null!;
    private string? _searchString;
    private bool _includeDeleted;
    private bool _isImporting;
    private bool _importFailed;
    private bool _importSucceeded;
    private string? _importMessage;

    private async Task<GridData<OrganizationalUnit>> LoadServerDataAsync(
        GridState<OrganizationalUnit> state,
        CancellationToken ct)
    {
        var sort = state.SortDefinitions.FirstOrDefault();
        var result = await OrganizationalUnitService.ListAsync(new ListQuery
        {
            SearchString = _searchString,
            ExcludeDeleted = !_includeDeleted,
            SortBy = sort?.SortBy,
            SortDescending = sort?.Descending ?? false,
            Skip = state.Page * state.PageSize,
            Take = state.PageSize
        }, ct);

        return new GridData<OrganizationalUnit>
        {
            Items = result.Items,
            TotalItems = result.TotalItems
        };
    }

    private Task OnSearchChangedAsync(string? value)
    {
        _searchString = value;
        return _dataGrid.ReloadServerData();
    }

    private Task OnIncludeDeletedChangedAsync(bool value)
    {
        _includeDeleted = value;
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
            var result = await CatalogImportService.ImportListAsync();
            if (!result.IsSuccess)
            {
                _importFailed = true;
                _importMessage = result.Error?.Message ?? "Не удалось обновить структуру предприятия из 1С.";
                return;
            }

            await _dataGrid.ReloadServerData();
            _importSucceeded = true;
            _importMessage = "Структура предприятия успешно обновлена из 1С.";
        }
        catch
        {
            _importFailed = true;
            _importMessage = "Не удалось обновить структуру предприятия из 1С.";
        }
        finally
        {
            _isImporting = false;
        }
    }
}

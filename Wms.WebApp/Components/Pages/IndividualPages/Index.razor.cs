using Microsoft.AspNetCore.Components;
using MudBlazor;
using Wms.Application.Services;
using Wms.Common;
using Wms.Domain;

namespace Wms.WebApp.Components.Pages.IndividualPages;

public partial class Index
{
    [Inject] private IndividualService IndividualService { get; set; } = null!;
    [Inject] private SynchronizedCatalogImportService SynchronizedCatalogImportService { get; set; } = null!;

    private MudDataGrid<Individual> _dataGrid = null!;
    private string? _searchString;
    private bool _includeDeleted;
    private bool _isImporting;
    private bool _importFailed;
    private bool _importSucceeded;
    private string? _importMessage;

    private async Task<GridData<Individual>> LoadServerDataAsync(
        GridState<Individual> state,
        CancellationToken ct)
    {
        var sort = state.SortDefinitions.FirstOrDefault();
        var result = await IndividualService.ListAsync(new ListQuery
        {
            SearchString = _searchString,
            ExcludeDeleted = !_includeDeleted,
            SortBy = sort?.SortBy,
            SortDescending = sort?.Descending ?? false,
            Skip = state.Page * state.PageSize,
            Take = state.PageSize
        }, ct);

        return new GridData<Individual>
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
            var result = await SynchronizedCatalogImportService.RefreshIndividualsAsync();
            if (!result.IsSuccess)
            {
                _importFailed = true;
                _importMessage = result.Error?.Message ?? "Не удалось обновить физических лиц из 1С.";
                return;
            }

            await _dataGrid.ReloadServerData();
            _importSucceeded = true;
            _importMessage = "Физические лица успешно обновлены из 1С.";
        }
        catch
        {
            _importFailed = true;
            _importMessage = "Не удалось обновить физических лиц из 1С.";
        }
        finally
        {
            _isImporting = false;
        }
    }
}

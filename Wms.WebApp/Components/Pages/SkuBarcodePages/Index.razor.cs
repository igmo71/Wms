using Microsoft.AspNetCore.Components;
using MudBlazor;
using Wms.Application.Services;
using Wms.Common;
using Wms.Domain;

namespace Wms.WebApp.Components.Pages.SkuBarcodePages;

public partial class Index
{
    [Inject] private SkuBarcodeService SkuBarcodeService { get; set; } = null!;
    private MudDataGrid<SkuBarcode> _dataGrid = null!;
    private string? _searchString;
    private bool _includeDeleted;
    private async Task<GridData<SkuBarcode>> LoadServerDataAsync(GridState<SkuBarcode> state, CancellationToken ct)
    {
        var sort = state.SortDefinitions.FirstOrDefault();
        var result = await SkuBarcodeService.ListAsync(new ListQuery { SearchString = _searchString, ExcludeDeleted = !_includeDeleted, SortBy = sort?.SortBy, SortDescending = sort?.Descending ?? false, Skip = state.Page * state.PageSize, Take = state.PageSize }, ct);
        return new GridData<SkuBarcode> { Items = result.Items, TotalItems = result.TotalItems };
    }
    private Task OnSearchChangedAsync(string? value) { _searchString = value; return _dataGrid.ReloadServerData(); }
    private Task OnIncludeDeletedChangedAsync(bool value) { _includeDeleted = value; return _dataGrid.ReloadServerData(); }
}

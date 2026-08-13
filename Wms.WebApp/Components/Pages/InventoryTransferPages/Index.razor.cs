using Microsoft.AspNetCore.Components;
using MudBlazor;
using Wms.Application.Services;
using Wms.Application.Services.Inventory;
using Wms.Common;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.WebApp.Components.Pages.InventoryTransferPages;

public partial class Index
{
    [Inject] private InventoryTransferQueryService InventoryTransferQueryService { get; set; } = null!;
    [Inject] private WarehouseService WarehouseService { get; set; } = null!;

    private MudDataGrid<InventoryTransfer> _dataGrid = null!;
    private string? _searchString;
    private DateTime? _dateFrom;
    private DateTime? _dateTo;
    private Warehouse? _warehouse;
    private InventoryTransferStatus? _status;

    private async Task<GridData<InventoryTransfer>> LoadServerDataAsync(
        GridState<InventoryTransfer> state,
        CancellationToken ct)
    {
        var sortDefinition = state.SortDefinitions.FirstOrDefault();
        var result = await InventoryTransferQueryService.ListAsync(new InventoryTransferListQuery
        {
            SearchString = _searchString,
            DateFrom = _dateFrom,
            DateTo = _dateTo,
            WarehouseId = _warehouse?.Id,
            Status = _status,
            SortBy = sortDefinition?.SortBy,
            SortDescending = sortDefinition?.Descending ?? true,
            Skip = state.Page * state.PageSize,
            Take = state.PageSize
        }, ct);

        return new GridData<InventoryTransfer>
        {
            Items = result.Items,
            TotalItems = result.TotalItems
        };
    }

    private Task OnSearchChangedAsync(string searchString)
    {
        _searchString = searchString;
        return _dataGrid.ReloadServerData();
    }

    private Task OnDateFromChangedAsync(DateTime? value)
    {
        _dateFrom = value;
        return _dataGrid.ReloadServerData();
    }

    private Task OnDateToChangedAsync(DateTime? value)
    {
        _dateTo = value;
        return _dataGrid.ReloadServerData();
    }

    private async Task<IEnumerable<Warehouse>> SearchWarehousesAsync(string? searchText, CancellationToken ct)
    {
        var result = await WarehouseService.ListAsync(new ListQuery
        {
            SearchString = searchText,
            SortBy = "Name",
            Take = 10
        }, ct);

        return result.Items;
    }

    private Task OnWarehouseChangedAsync(Warehouse? value)
    {
        _warehouse = value;
        return _dataGrid.ReloadServerData();
    }

    private Task OnStatusChangedAsync(InventoryTransferStatus? value)
    {
        _status = value;
        return _dataGrid.ReloadServerData();
    }
}

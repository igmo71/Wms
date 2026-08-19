using Microsoft.AspNetCore.Components;
using MudBlazor;
using Wms.Application.Services;
using Wms.Application.Services.Inventory;
using Wms.Application.StorageLocations;
using Wms.Common;
using Wms.Domain;

namespace Wms.WebApp.Components.Pages.InventoryBalancePages;

public partial class Index
{
    [Inject]
    private InventoryBalanceService InventoryBalanceService { get; set; } = null!;

    [Inject]
    private WarehouseService WarehouseService { get; set; } = null!;

    [Inject]
    private StorageLocationService StorageLocationService { get; set; } = null!;

    [Inject]
    private StockKeepingUnitService StockKeepingUnitService { get; set; } = null!;

    private MudDataGrid<InventoryBalance> _dataGrid = null!;
    private string? _searchString;
    private Warehouse? _warehouse;
    private StorageLocation? _storageLocation;
    private StockKeepingUnit? _stockKeepingUnit;

    private async Task<GridData<InventoryBalance>> LoadServerDataAsync(
        GridState<InventoryBalance> state,
        CancellationToken cancellationToken)
    {
        var sortDefinition = state.SortDefinitions.FirstOrDefault();
        var query = new InventoryBalanceListQuery
        {
            SearchString = _searchString,
            WarehouseId = _warehouse?.Id,
            StorageLocationId = _storageLocation?.Id,
            StockKeepingUnitId = _stockKeepingUnit?.Id,
            SortBy = sortDefinition?.SortBy,
            SortDescending = sortDefinition?.Descending ?? false,
            Skip = state.Page * state.PageSize,
            Take = state.PageSize
        };

        var result = await InventoryBalanceService.ListAsync(query, cancellationToken);

        return new GridData<InventoryBalance>
        {
            Items = result.Items,
            TotalItems = result.TotalItems
        };
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

    private async Task<IEnumerable<StorageLocation>> SearchStorageLocationsAsync(string? searchText, CancellationToken ct)
    {
        var result = await StorageLocationService.ListAsync(new StorageLocationListQuery
        {
            SearchString = searchText,
            WarehouseId = _warehouse?.Id,
            SortBy = "Name",
            Take = 10
        }, ct);

        return result.Items;
    }

    private async Task<IEnumerable<StockKeepingUnit>> SearchStockKeepingUnitsAsync(string? searchText, CancellationToken ct)
    {
        var result = await StockKeepingUnitService.ListAsync(new ListQuery
        {
            SearchString = searchText,
            SortBy = "Name",
            Take = 10
        }, ct);

        return result.Items;
    }

    private Task OnSearchChangedAsync(string? searchString)
    {
        _searchString = searchString;
        return _dataGrid.ReloadServerData();
    }

    private Task OnWarehouseChangedAsync(Warehouse? warehouse)
    {
        _warehouse = warehouse;
        _storageLocation = null;
        return _dataGrid.ReloadServerData();
    }

    private Task OnStorageLocationChangedAsync(StorageLocation? storageLocation)
    {
        _storageLocation = storageLocation;
        if (storageLocation?.Warehouse is not null)
            _warehouse = storageLocation.Warehouse;

        return _dataGrid.ReloadServerData();
    }

    private Task OnStockKeepingUnitChangedAsync(StockKeepingUnit? stockKeepingUnit)
    {
        _stockKeepingUnit = stockKeepingUnit;
        return _dataGrid.ReloadServerData();
    }
}

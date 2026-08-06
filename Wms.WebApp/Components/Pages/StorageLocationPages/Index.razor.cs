using Microsoft.AspNetCore.Components;
using MudBlazor;
using Wms.Application;
using Wms.Common;
using Wms.Domain;

namespace Wms.WebApp.Components.Pages.StorageLocationPages;

public partial class Index
{
    [Inject]
    private StorageLocationService StorageLocationService { get; set; } = null!;

    [Inject]
    private WarehouseService WarehouseService { get; set; } = null!;

    [Inject]
    private ZoneService ZoneService { get; set; } = null!;

    [Inject]
    private IDialogService DialogService { get; set; } = null!;

    private MudDataGrid<StorageLocation> _dataGrid = null!;
    private string? _searchString;
    private Warehouse? _warehouse;
    private Zone? _zone;
    private bool _includeDeleted;

    private async Task<GridData<StorageLocation>> LoadServerDataAsync(
        GridState<StorageLocation> state,
        CancellationToken cancellationToken)
    {
        var sortDefinition = state.SortDefinitions.FirstOrDefault();
        var query = new StorageLocationListQuery
        {
            SearchString = _searchString,
            WarehouseId = _warehouse?.Id,
            ZoneId = _zone?.Id,
            ExcludeDeleted = !_includeDeleted,
            SortBy = sortDefinition?.SortBy,
            SortDescending = sortDefinition?.Descending ?? false,
            Skip = state.Page * state.PageSize,
            Take = state.PageSize
        };

        var result = await StorageLocationService.ListAsync(query, cancellationToken);

        return new GridData<StorageLocation>
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

    private async Task<IEnumerable<Zone>> SearchZonesAsync(string? searchText, CancellationToken ct)
    {
        var result = await ZoneService.ListAsync(new ZoneListQuery
        {
            SearchString = searchText,
            WarehouseId = _warehouse?.Id,
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
        _zone = null;
        return _dataGrid.ReloadServerData();
    }

    private Task OnZoneChangedAsync(Zone? zone)
    {
        _zone = zone;
        if (zone?.Warehouse is not null)
            _warehouse = zone.Warehouse;

        return _dataGrid.ReloadServerData();
    }

    private Task OnIncludeDeletedChangedAsync(bool includeDeleted)
    {
        _includeDeleted = includeDeleted;
        return _dataGrid.ReloadServerData();
    }

    private Task CreateStorageLocationAsync() => ShowStorageLocationDialogAsync(null);

    private Task EditStorageLocationAsync(StorageLocation storageLocation) =>
        ShowStorageLocationDialogAsync(storageLocation);

    private async Task ShowStorageLocationDialogAsync(StorageLocation? storageLocation)
    {
        var parameters = new DialogParameters<StorageLocationDialog>
        {
            { x => x.StorageLocation, storageLocation }
        };

        var dialog = await DialogService.ShowAsync<StorageLocationDialog>(
            storageLocation is null ? "Создать ячейку" : "Редактировать ячейку",
            parameters);

        var result = await dialog.Result;
        if (result is not null && !result.Canceled)
            await _dataGrid.ReloadServerData();
    }

    private async Task MarkDeleteAsync(Guid id)
    {
        await StorageLocationService.MarkDeleteAsync(id);
        await _dataGrid.ReloadServerData();
    }

    private async Task UnMarkDeleteAsync(Guid id)
    {
        await StorageLocationService.UnMarkDeleteAsync(id);
        await _dataGrid.ReloadServerData();
    }
}

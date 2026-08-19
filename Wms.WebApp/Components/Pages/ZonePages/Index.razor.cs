using Microsoft.AspNetCore.Components;
using MudBlazor;
using Wms.Application.Services;
using Wms.Application.Zones;
using Wms.Common;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.WebApp.Components.Pages.ZonePages;

public partial class Index
{
    [Inject]
    private ZoneService ZoneService { get; set; } = null!;

    [Inject]
    private WarehouseService WarehouseService { get; set; } = null!;

    [Inject]
    private IDialogService DialogService { get; set; } = null!;

    private MudDataGrid<Zone> _dataGrid = null!;
    private string? _searchString;
    private Warehouse? _warehouse;
    private ZoneType? _zoneType;
    private bool _includeDeleted;

    private async Task<GridData<Zone>> LoadServerDataAsync(
        GridState<Zone> state,
        CancellationToken cancellationToken)
    {
        var sortDefinition = state.SortDefinitions.FirstOrDefault();
        var query = new ZoneListQuery
        {
            SearchString = _searchString,
            WarehouseId = _warehouse?.Id,
            Type = _zoneType,
            ExcludeDeleted = !_includeDeleted,
            SortBy = sortDefinition?.SortBy,
            SortDescending = sortDefinition?.Descending ?? false,
            Skip = state.Page * state.PageSize,
            Take = state.PageSize
        };

        var result = await ZoneService.ListAsync(query, cancellationToken);

        return new GridData<Zone>
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

    private Task OnSearchChangedAsync(string? searchString)
    {
        _searchString = searchString;
        return _dataGrid.ReloadServerData();
    }

    private Task OnWarehouseChangedAsync(Warehouse? warehouse)
    {
        _warehouse = warehouse;
        return _dataGrid.ReloadServerData();
    }

    private Task OnZoneTypeChangedAsync(ZoneType? zoneType)
    {
        _zoneType = zoneType;
        return _dataGrid.ReloadServerData();
    }

    private Task OnIncludeDeletedChangedAsync(bool includeDeleted)
    {
        _includeDeleted = includeDeleted;
        return _dataGrid.ReloadServerData();
    }

    private Task CreateZoneAsync() => ShowZoneDialogAsync(null);

    private Task EditZoneAsync(Zone zone) => ShowZoneDialogAsync(zone);

    private async Task ShowZoneDialogAsync(Zone? zone)
    {
        var parameters = new DialogParameters<ZoneDialog>
        {
            { x => x.Zone, zone }
        };

        var dialog = await DialogService.ShowAsync<ZoneDialog>(
            zone is null ? "Создать зону" : "Редактировать зону",
            parameters);

        var result = await dialog.Result;
        if (result is not null && !result.Canceled)
            await _dataGrid.ReloadServerData();
    }

    private async Task MarkDeleteAsync(Guid id)
    {
        await ZoneService.MarkDeleteAsync(id);
        await _dataGrid.ReloadServerData();
    }

    private async Task UnMarkDeleteAsync(Guid id)
    {
        await ZoneService.UnMarkDeleteAsync(id);
        await _dataGrid.ReloadServerData();
    }
}

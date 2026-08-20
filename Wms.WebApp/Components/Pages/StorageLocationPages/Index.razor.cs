using Microsoft.AspNetCore.Components;
using MudBlazor;
using Wms.Application.StorageLocations;
using Wms.Application.Warehouses;
using Wms.Application.Zones;
using Wms.Common;
using Wms.Domain;

namespace Wms.WebApp.Components.Pages.StorageLocationPages;

public partial class Index
{
    [Inject] private StorageLocationCommandService StorageLocationCommandService { get; set; } = null!;
    [Inject] private StorageLocationQueryService StorageLocationQueryService { get; set; } = null!;
    [Inject] private WarehouseService WarehouseService { get; set; } = null!;
    [Inject] private ZoneQueryService ZoneQueryService { get; set; } = null!;
    [Inject] private IDialogService DialogService { get; set; } = null!;

    private Warehouse? _warehouse;
    private Zone? _zone;
    private StorageLocation? _selectedLocation;
    private IReadOnlyList<StorageLocation> _locations = [];
    private bool _includeDeleted;
    private bool _isLoading;
    private string? _errorMessage;

    private string CoordinatesText => _selectedLocation is null
        ? "—"
        : $"X: {Format(_selectedLocation.Coordinates.X)}, Y: {Format(_selectedLocation.Coordinates.Y)}, Z: {Format(_selectedLocation.Coordinates.Z)}";

    private IEnumerable<StorageLocation> GetChildren(Guid? parentId) =>
        _locations.Where(x => x.ParentId == parentId).OrderBy(x => x.Code);

    private async Task<IEnumerable<Warehouse>> SearchWarehousesAsync(string? searchText, CancellationToken ct)
    {
        var result = await WarehouseService.ListAsync(new ListQuery
        {
            SearchString = searchText,
            SortBy = "Name",
            Take = 20
        }, ct);
        return result.Items;
    }

    private async Task<IEnumerable<Zone>> SearchZonesAsync(string? searchText, CancellationToken ct)
    {
        var result = await ZoneQueryService.ListAsync(new ZoneListQuery
        {
            SearchString = searchText,
            WarehouseId = _warehouse?.Id,
            SortBy = "Name",
            Take = 50
        }, ct);
        return result.Items;
    }

    private async Task OnWarehouseChangedAsync(Warehouse? warehouse)
    {
        _warehouse = warehouse;
        _zone = null;
        _selectedLocation = null;
        _locations = [];
        await Task.CompletedTask;
    }

    private async Task OnZoneChangedAsync(Zone? zone)
    {
        _zone = zone;
        _selectedLocation = null;
        if (zone?.Warehouse is not null)
            _warehouse = zone.Warehouse;
        await LoadTreeAsync();
    }

    private async Task OnIncludeDeletedChangedAsync(bool includeDeleted)
    {
        _includeDeleted = includeDeleted;
        await LoadTreeAsync();
    }

    private Task OnSelectedLocationChanged(StorageLocation? location)
    {
        _selectedLocation = location;
        return Task.CompletedTask;
    }

    private Task CreateRootAsync() => ShowLocationDialogAsync(null, null);
    private Task CreateChildAsync() => ShowLocationDialogAsync(null, _selectedLocation);
    private Task EditSelectedAsync() => ShowLocationDialogAsync(_selectedLocation, null);

    private async Task ShowLocationDialogAsync(StorageLocation? location, StorageLocation? parent)
    {
        if (_warehouse is null || _zone is null)
            return;

        var parameters = new DialogParameters<StorageLocationDialog>
        {
            { x => x.Warehouse, _warehouse },
            { x => x.Zone, _zone },
            { x => x.Parent, parent },
            { x => x.StorageLocation, location }
        };
        var dialog = await DialogService.ShowAsync<StorageLocationDialog>(
            location is null ? "Создать складскую позицию" : "Изменить складскую позицию", parameters);
        var result = await dialog.Result;
        if (result is not null && !result.Canceled)
            await LoadTreeAsync();
    }

    private async Task GenerateChildrenAsync()
    {
        if (_warehouse is null || _zone is null)
            return;

        var parameters = new DialogParameters<GenerateStorageLocationsDialog>
        {
            { x => x.Warehouse, _warehouse },
            { x => x.Zone, _zone },
            { x => x.Parent, _selectedLocation }
        };
        var dialog = await DialogService.ShowAsync<GenerateStorageLocationsDialog>("Пакетное создание", parameters);
        var result = await dialog.Result;
        if (result is not null && !result.Canceled)
            await LoadTreeAsync();
    }

    private async Task DeactivateSelectedAsync()
    {
        if (_selectedLocation is null)
            return;
        var result = await StorageLocationCommandService.MarkDeleteAsync(_selectedLocation.Id);
        await HandleActionResultAsync(result);
    }

    private async Task ActivateSelectedAsync()
    {
        if (_selectedLocation is null)
            return;
        var result = await StorageLocationCommandService.UnMarkDeleteAsync(_selectedLocation.Id);
        await HandleActionResultAsync(result);
    }

    private async Task HandleActionResultAsync(OperationResult result)
    {
        if (!result.IsSuccess)
        {
            _errorMessage = result.Error?.Message ?? "Операция не выполнена.";
            return;
        }
        await LoadTreeAsync();
    }

    private async Task LoadTreeAsync()
    {
        _errorMessage = null;
        _selectedLocation = null;
        if (_zone is null)
        {
            _locations = [];
            return;
        }

        _isLoading = true;
        try
        {
            _locations = await StorageLocationQueryService.GetTreeAsync(_zone.Id, _includeDeleted);
        }
        catch
        {
            _locations = [];
            _errorMessage = "Не удалось загрузить дерево складских позиций.";
        }
        finally
        {
            _isLoading = false;
        }
    }

    private static string Format(double? value) => value?.ToString("0.###") ?? "—";
    private static string ZoneText(Zone? zone) => zone is null ? string.Empty : $"{zone.Code} · {zone.Name}";
}

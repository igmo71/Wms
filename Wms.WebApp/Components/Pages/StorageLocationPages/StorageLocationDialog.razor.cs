using Microsoft.AspNetCore.Components;
using MudBlazor;
using Wms.Application.Services;
using Wms.Common;
using Wms.Domain;

namespace Wms.WebApp.Components.Pages.StorageLocationPages;

public partial class StorageLocationDialog
{
    [CascadingParameter]
    private IMudDialogInstance MudDialog { get; set; } = null!;

    [Parameter]
    public StorageLocation? StorageLocation { get; set; }

    [Inject]
    private StorageLocationService StorageLocationService { get; set; } = null!;

    [Inject]
    private WarehouseService WarehouseService { get; set; } = null!;

    [Inject]
    private ZoneService ZoneService { get; set; } = null!;

    private StorageLocation _storageLocation = null!;
    private Warehouse? _warehouse;
    private Zone? _zone;

    protected override void OnInitialized()
    {
        _storageLocation = StorageLocation is null
            ? new StorageLocation { Id = Guid.NewGuid() }
            : new StorageLocation
            {
                Id = StorageLocation.Id,
                Name = StorageLocation.Name,
                DeletionMark = StorageLocation.DeletionMark,
                WarehouseId = StorageLocation.WarehouseId,
                ZoneId = StorageLocation.ZoneId
            };

        _warehouse = StorageLocation?.Warehouse;
        _zone = StorageLocation?.Zone;
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

    private Task OnWarehouseChanged(Warehouse? warehouse)
    {
        _warehouse = warehouse;
        _zone = null;
        return Task.CompletedTask;
    }

    private Task OnZoneChanged(Zone? zone)
    {
        _zone = zone;
        if (zone?.Warehouse is not null)
            _warehouse = zone.Warehouse;

        return Task.CompletedTask;
    }

    private async Task SaveAsync()
    {
        if (_warehouse is null || _zone is null || string.IsNullOrWhiteSpace(_storageLocation.Name))
            return;

        _storageLocation.WarehouseId = _warehouse.Id;
        _storageLocation.ZoneId = _zone.Id;
        await StorageLocationService.CreateOrUpdateAsync(_storageLocation);

        MudDialog.Close(DialogResult.Ok(_storageLocation));
    }

    private void Cancel() => MudDialog.Cancel();
}

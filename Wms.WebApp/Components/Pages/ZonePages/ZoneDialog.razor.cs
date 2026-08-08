using Microsoft.AspNetCore.Components;
using MudBlazor;
using Wms.Application.Services;
using Wms.Common;
using Wms.Domain;

namespace Wms.WebApp.Components.Pages.ZonePages;

public partial class ZoneDialog
{
    [CascadingParameter]
    private IMudDialogInstance MudDialog { get; set; } = null!;

    [Parameter]
    public Zone? Zone { get; set; }

    [Inject]
    private ZoneService ZoneService { get; set; } = null!;

    [Inject]
    private WarehouseService WarehouseService { get; set; } = null!;

    private Zone _zone = null!;
    private Warehouse? _warehouse;

    protected override void OnInitialized()
    {
        _zone = Zone is null
            ? new Zone { Id = Guid.NewGuid() }
            : new Zone
            {
                Id = Zone.Id,
                Name = Zone.Name,
                DeletionMark = Zone.DeletionMark,
                WarehouseId = Zone.WarehouseId
            };

        _warehouse = Zone?.Warehouse;
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

    private Task OnWarehouseChanged(Warehouse? warehouse)
    {
        _warehouse = warehouse;
        return Task.CompletedTask;
    }

    private async Task SaveAsync()
    {
        if (_warehouse is null || string.IsNullOrWhiteSpace(_zone.Name))
            return;

        _zone.WarehouseId = _warehouse.Id;
        await ZoneService.CreateOrUpdateAsync(_zone);

        MudDialog.Close(DialogResult.Ok(_zone));
    }

    private void Cancel() => MudDialog.Cancel();
}

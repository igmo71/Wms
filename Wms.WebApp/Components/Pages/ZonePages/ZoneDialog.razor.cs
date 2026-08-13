using Microsoft.AspNetCore.Components;
using MudBlazor;
using Wms.Application.Services;
using Wms.Common;
using Wms.Domain;
using Wms.Domain.Enums;

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
    private ZoneType? _zoneType;
    private bool _isSaving;
    private string? _errorMessage;

    protected override void OnInitialized()
    {
        _zone = Zone is null
            ? new Zone { Id = Guid.NewGuid() }
            : new Zone
            {
                Id = Zone.Id,
                Name = Zone.Name,
                DeletionMark = Zone.DeletionMark,
                WarehouseId = Zone.WarehouseId,
                Type = Zone.Type
            };

        _warehouse = Zone?.Warehouse;
        _zoneType = Zone?.Type;
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
        if (_warehouse is null || _zoneType is null || string.IsNullOrWhiteSpace(_zone.Name))
            return;

        _zone.WarehouseId = _warehouse.Id;
        _zone.Type = _zoneType.Value;
        _isSaving = true;
        _errorMessage = null;

        try
        {
            var result = await ZoneService.CreateOrUpdateAsync(_zone);
            if (!result.IsSuccess)
            {
                _errorMessage = result.Error?.Message ?? "Не удалось сохранить зону.";
                return;
            }

            MudDialog.Close(DialogResult.Ok(_zone));
        }
        catch
        {
            _errorMessage = "Не удалось сохранить зону.";
        }
        finally
        {
            _isSaving = false;
        }
    }

    private void Cancel() => MudDialog.Cancel();
}

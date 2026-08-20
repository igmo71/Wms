using Microsoft.AspNetCore.Components;
using MudBlazor;
using Wms.Application.Warehouses;
using Wms.Application.Zones;
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
    private ZoneCommandService ZoneCommandService { get; set; } = null!;

    [Inject]
    private WarehouseService WarehouseService { get; set; } = null!;

    private Warehouse? _warehouse;
    private ZoneType? _zoneType;
    private string _code = string.Empty;
    private string _name = string.Empty;
    private bool _isSaving;
    private string? _errorMessage;

    protected override void OnInitialized()
    {
        _warehouse = Zone?.Warehouse;
        _zoneType = Zone?.Type;
        _code = Zone?.Code ?? string.Empty;
        _name = Zone?.Name ?? string.Empty;
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
        if (_warehouse is null || _zoneType is null || string.IsNullOrWhiteSpace(_code)
            || string.IsNullOrWhiteSpace(_name))
        {
            return;
        }

        _isSaving = true;
        _errorMessage = null;

        try
        {
            var result = await ZoneCommandService.SaveAsync(new SaveZoneCommand
            {
                Id = Zone?.Id,
                WarehouseId = _warehouse.Id,
                Code = _code,
                Name = _name,
                Type = _zoneType.Value
            });

            if (!result.IsSuccess)
            {
                _errorMessage = result.Error?.Message ?? "Не удалось сохранить зону.";
                return;
            }

            MudDialog.Close(DialogResult.Ok(result.Value));
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

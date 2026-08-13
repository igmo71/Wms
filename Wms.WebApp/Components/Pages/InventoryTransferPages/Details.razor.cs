using Microsoft.AspNetCore.Components;
using Wms.Application.Services.Inventory;
using Wms.Domain;

namespace Wms.WebApp.Components.Pages.InventoryTransferPages;

public partial class Details
{
    [Parameter] public Guid Id { get; set; }

    [Inject] private InventoryTransferQueryService InventoryTransferQueryService { get; set; } = null!;

    private InventoryTransfer? _transfer;
    private List<InventoryMovement> _movements = [];
    private bool _isLoading = true;

    protected override async Task OnParametersSetAsync()
    {
        _isLoading = true;
        _transfer = await InventoryTransferQueryService.GetAsync(Id);
        _movements = _transfer is null ? [] : await InventoryTransferQueryService.GetMovementsAsync(Id);
        _isLoading = false;
    }

    private static string FormatDateTimeOffset(DateTimeOffset? value) =>
        value?.ToLocalTime().ToString("dd.MM.yyyy HH:mm") ?? "—";

    private static string FormatQuantity(double value) => value.ToString("0.###");
}

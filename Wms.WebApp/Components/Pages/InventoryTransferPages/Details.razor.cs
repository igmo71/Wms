using Microsoft.AspNetCore.Components;
using Wms.Application.Services;
using Wms.Application.Services.Inventory;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.WebApp.Components.Pages.InventoryTransferPages;

public partial class Details
{
    [Parameter] public Guid Id { get; set; }

    [Inject] private InventoryTransferQueryService InventoryTransferQueryService { get; set; } = null!;
    [Inject] private ApplicationUserQueryService ApplicationUserQueryService { get; set; } = null!;

    private InventoryTransfer? _transfer;
    private List<InventoryMovement> _movements = [];
    private bool _isLoading = true;
    private IReadOnlyDictionary<string, string> _userNames = new Dictionary<string, string>();

    protected override async Task OnParametersSetAsync()
    {
        _isLoading = true;
        _transfer = await InventoryTransferQueryService.GetAsync(Id);
        _movements = _transfer is null ? [] : await InventoryTransferQueryService.GetMovementsAsync(Id);
        _userNames = _transfer is null
            ? new Dictionary<string, string>()
            : await ApplicationUserQueryService.GetUserNamesAsync([
                _transfer.CreatedBy,
                _transfer.StartedBy,
                _transfer.CompletedBy]);
        _isLoading = false;
    }

    private static string FormatDateTimeOffset(DateTimeOffset? value) =>
        value?.ToLocalTime().ToString("dd.MM.yyyy HH:mm") ?? "—";

    private static string FormatQuantity(double value) => value.ToString("0.###");

    private string GetUserName(string? userId) => string.IsNullOrWhiteSpace(userId)
        ? "—"
        : _userNames.TryGetValue(userId, out var userName)
            ? userName
            : "Пользователь не найден";

    private double KnownPlacedWeightKg => _movements
        .Where(IsPlacedInStorage)
        .Sum(x => x.WeightKg ?? 0);

    private bool IsPlacedWeightComplete => _movements
        .Where(IsPlacedInStorage)
        .All(x => x.Quantity == 0 || x.WeightKg.HasValue);

    private static bool IsPlacedInStorage(InventoryMovement movement) =>
        movement.DestinationStorageLocation?.Zone?.Type == ZoneType.Storage;
}

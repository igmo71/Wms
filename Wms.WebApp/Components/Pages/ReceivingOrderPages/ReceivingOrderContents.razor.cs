using Microsoft.AspNetCore.Components;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.WebApp.Components.Pages.ReceivingOrderPages;

public partial class ReceivingOrderContents
{
    [Parameter, EditorRequired]
    public ReceivingOrder Order { get; set; } = null!;

    [Parameter]
    public IReadOnlyDictionary<string, string> UserNames { get; set; } =
        new Dictionary<string, string>();

    [Parameter]
    public bool ShowCurrentPlan { get; set; }

    [Parameter]
    public Func<ReceivingOrderItem, decimal?>? CurrentPlanQuantity { get; set; }

    private bool _showSeparatePlans;
    private string PlanColumnTitle => _showSeparatePlans ? "План WMS" : "План";
    private object? OpenPalletBadge
    {
        get
        {
            int count = Order.LicensePlateNumbers.Count(x => x.Status == LicensePlateNumberStatus.Open);
            if (count == 0)
                return null;
            return count;
        }
    }
    private Guid? _selectedPalletId;

    private IReadOnlyList<LicensePlateNumber> OrderedPallets => Order.LicensePlateNumbers
        .OrderBy(StatusOrder)
        .ThenByDescending(x => x.CreatedAtUtc)
        .ToList();

    private LicensePlateNumber? SelectedPallet => Order.LicensePlateNumbers
        .SingleOrDefault(x => x.Id == _selectedPalletId);

    protected override void OnParametersSet()
    {
        _showSeparatePlans = ShowCurrentPlan
            && (CurrentPlanQuantity is null
                || Order.Items.Any(item => CurrentPlanQuantity(item) != item.PlanQuantity));

        if (_selectedPalletId is Guid selectedId
            && Order.LicensePlateNumbers.Any(x => x.Id == selectedId))
        {
            return;
        }

        _selectedPalletId = OrderedPallets
            .FirstOrDefault(x => x.Status != LicensePlateNumberStatus.Cancelled)?.Id
            ?? OrderedPallets.FirstOrDefault()?.Id;
    }

    private void SelectPallet(Guid palletId) => _selectedPalletId = palletId;

    private string GetUserName(string? userId) => string.IsNullOrWhiteSpace(userId)
        ? "—"
        : UserNames.TryGetValue(userId, out var userName)
            ? userName
            : "Пользователь не найден";

    private string PalletStyle(LicensePlateNumber pallet)
    {
        var background = pallet.Id == _selectedPalletId ? "background-color: var(--mud-palette-action-default-hover);" : string.Empty;
        var opacity = pallet.Status == LicensePlateNumberStatus.Cancelled ? "opacity: 0.55;" : string.Empty;
        return $"cursor: pointer; {background} {opacity}";
    }

    private static int StatusOrder(LicensePlateNumber pallet) => pallet.Status switch
    {
        LicensePlateNumberStatus.Open => 0,
        LicensePlateNumberStatus.Closed => 1,
        LicensePlateNumberStatus.Available => 2,
        LicensePlateNumberStatus.Cancelled => 3,
        _ => 4
    };

    private static string FormatQuantity(decimal? quantity, string emptyText = "—") =>
        quantity?.ToString("N3") ?? emptyText;

    private static string UnitName(LicensePlateNumberItem item) => UnitName(item.StockKeepingUnit);

    private static string UnitName(StockKeepingUnit? sku) =>
        sku?.BaseUnitOfMeasure?.Description
        ?? sku?.BaseUnitOfMeasure?.Abbreviation
        ?? sku?.BaseUnitOfMeasure?.Name
        ?? "—";

    private static string Destination(LicensePlateNumberPutaway putaway) =>
        $"{putaway.DestinationStorageLocation.Zone.Code}-{putaway.DestinationStorageLocation.Code}";
}

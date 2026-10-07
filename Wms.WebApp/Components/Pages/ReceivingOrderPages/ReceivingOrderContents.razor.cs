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
    private int _activeTabIndex;
    private Guid? _selectedPalletId;
    private IReadOnlyList<LicensePlateNumber> GetSkuPallets(Guid skuId) => OrderedPallets
            .Where(pallet => pallet.Status != LicensePlateNumberStatus.Cancelled
                && pallet.Items.Any(item => !item.IsDeleted && item.StockKeepingUnitId == skuId))
            .ToList();

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

    private void OpenPallet(Guid palletId)
    {
        _selectedPalletId = palletId;
        _activeTabIndex = 1;
    }

    private static decimal GetSkuQuantity(LicensePlateNumber pallet, Guid skuId) => pallet.Items
        .Where(item => !item.IsDeleted && item.StockKeepingUnitId == skuId)
        .Sum(item => item.Quantity);

    private static decimal PlacedQuantity(LicensePlateNumber pallet, LicensePlateNumberItem item) =>
        pallet.Putaways.Where(x => x.LicensePlateNumberItemId == item.Id).Sum(x => x.Quantity);

    private static int UnplacedLineCount(LicensePlateNumber pallet) => pallet.Items
        .Count(item => !item.IsDeleted && item.Quantity > PlacedQuantity(pallet, item));

    private static string PutawayQuantityText(LicensePlateNumber pallet, LicensePlateNumberItem item) =>
        item.IsDeleted ? "—" : FormatQuantity(PlacedQuantity(pallet, item));

    private static string PutawayRemainingText(LicensePlateNumber pallet, LicensePlateNumberItem item) =>
        item.IsDeleted ? "—" : FormatQuantity(Math.Max(0, item.Quantity - PlacedQuantity(pallet, item)));

    private static string PutawayProgress(LicensePlateNumber pallet)
    {
        var items = pallet.Items.Where(x => !x.IsDeleted && x.Quantity > 0).ToArray();
        if (items.Length == 0)
            return "нет товара для размещения";
        if (items.All(item => PlacedQuantity(pallet, item) >= item.Quantity))
            return "завершено";
        return items.Any(item => PlacedQuantity(pallet, item) > 0)
            ? "частично"
            : "не начато";
    }

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

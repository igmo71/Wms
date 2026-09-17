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
    public string CurrentPlanTitle { get; set; } = "Актуальный план 1С";

    [Parameter]
    public Func<ReceivingOrderItem, decimal?>? CurrentPlanQuantity { get; set; }

    private Guid? _selectedPalletId;

    private IReadOnlyList<LicensePlateNumber> OrderedPallets => Order.LicensePlateNumbers
        .OrderBy(StatusOrder)
        .ThenByDescending(x => x.CreatedAtUtc)
        .ToList();

    private LicensePlateNumber? SelectedPallet => Order.LicensePlateNumbers
        .SingleOrDefault(x => x.Id == _selectedPalletId);

    private string ParticipantsText => Order.Participants.Count == 0
        ? "К ордеру ещё никто не присоединился."
        : string.Join(" · ", Order.Participants
            .OrderBy(x => x.JoinedAtUtc)
            .Select(x => $"{GetUserName(x.UserId)} — {x.JoinedAtUtc:dd.MM.yyyy HH:mm}"));

    protected override void OnParametersSet()
    {
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
        quantity?.ToString("0.###") ?? emptyText;

    private static string UnitName(LicensePlateNumberItem item) =>
        item.StockKeepingUnit?.BaseUnitOfMeasure?.Description
        ?? item.StockKeepingUnit?.BaseUnitOfMeasure?.Abbreviation
        ?? "—";

    private static string Destination(LicensePlateNumberPutaway putaway) =>
        $"{putaway.DestinationStorageLocation.Zone.Code}-{putaway.DestinationStorageLocation.Code}";
}

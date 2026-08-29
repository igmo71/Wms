using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Wms.Contracts.Mobile.V1;

namespace Wms.Mobile;

public sealed class InventoryCountItemViewState : INotifyPropertyChanged
{
    private string _quantityText = string.Empty;

    public Guid? ItemId { get; private set; }
    public Guid StockKeepingUnitId { get; private set; }
    public string SkuName { get; private set; } = string.Empty;
    public string? UnitOfMeasure { get; private set; }
    public double ExpectedQuantity { get; private set; }
    public double? CountedQuantity { get; private set; }
    public double? DifferenceQuantity { get; private set; }
    public bool IsExpected { get; private set; }
    public bool IsPending { get; private set; }
    public bool CanEdit { get; private set; }
    public bool IsEditing { get; private set; }
    public bool IsAccented { get; private set; }
    public string? AccentText { get; private set; }
    public bool HasQuantityError { get; private set; }
    public bool ActionsEnabled { get; private set; }

    public string QuantitySummary =>
        $"Ожид.: {Format(ExpectedQuantity)} · Факт: {Format(CountedQuantity)} · Разн.: {Format(DifferenceQuantity)} {UnitOfMeasure}";
    public bool HasAccentText => !string.IsNullOrEmpty(AccentText);
    public bool ShowActions => CanEdit && !IsEditing;
    public bool ShowRemoveAction => ShowActions && !IsExpected;

    public string QuantityText
    {
        get => _quantityText;
        set
        {
            if (_quantityText == value)
                return;

            var hadQuantityError = HasQuantityError;
            _quantityText = value;
            HasQuantityError = false;
            OnPropertyChanged();
            if (hadQuantityError)
                OnPropertyChanged(nameof(HasQuantityError));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public static InventoryCountItemViewState FromItem(
        MobileInventoryCountItemResponse item,
        bool canEdit)
    {
        var state = new InventoryCountItemViewState();
        state.Update(item, canEdit);
        return state;
    }

    public static InventoryCountItemViewState FromPendingSku(
        MobileInventoryCountSkuSearchResponse sku)
    {
        var state = new InventoryCountItemViewState
        {
            StockKeepingUnitId = sku.Id,
            SkuName = sku.Name,
            UnitOfMeasure = sku.UnitOfMeasure,
            ExpectedQuantity = 0,
            CountedQuantity = null,
            DifferenceQuantity = null,
            IsExpected = false,
            IsPending = true,
            CanEdit = true
        };
        return state;
    }

    public void Update(MobileInventoryCountItemResponse item, bool canEdit)
    {
        var changed = ItemId != item.Id
            || StockKeepingUnitId != item.StockKeepingUnitId
            || SkuName != item.SkuName
            || UnitOfMeasure != item.UnitOfMeasure
            || ExpectedQuantity != item.ExpectedQuantity
            || CountedQuantity != item.CountedQuantity
            || DifferenceQuantity != item.DifferenceQuantity
            || IsExpected != item.IsExpected
            || IsPending
            || CanEdit != canEdit;

        ItemId = item.Id;
        StockKeepingUnitId = item.StockKeepingUnitId;
        SkuName = item.SkuName;
        UnitOfMeasure = item.UnitOfMeasure;
        ExpectedQuantity = item.ExpectedQuantity;
        CountedQuantity = item.CountedQuantity;
        DifferenceQuantity = item.DifferenceQuantity;
        IsExpected = item.IsExpected;
        IsPending = false;
        CanEdit = canEdit;
        if (changed)
            OnPropertyChanged(string.Empty);
    }

    public void BeginEditing()
    {
        QuantityText = CountedQuantity?.ToString("0.###", CultureInfo.InvariantCulture)
            ?? string.Empty;
        IsEditing = true;
        ClearQuantityError();
        OnPropertyChanged(nameof(IsEditing));
        OnPropertyChanged(nameof(ShowActions));
        OnPropertyChanged(nameof(ShowRemoveAction));
    }

    public void EndEditing()
    {
        IsEditing = false;
        ClearQuantityError();
        OnPropertyChanged(nameof(IsEditing));
        OnPropertyChanged(nameof(ShowActions));
        OnPropertyChanged(nameof(ShowRemoveAction));
    }

    public void SetAccent(bool isAccented, string? text)
    {
        var accentText = isAccented ? text : null;
        if (IsAccented == isAccented && AccentText == accentText)
            return;

        IsAccented = isAccented;
        AccentText = accentText;
        OnPropertyChanged(nameof(IsAccented));
        OnPropertyChanged(nameof(AccentText));
        OnPropertyChanged(nameof(HasAccentText));
    }

    public void SetActionsEnabled(bool enabled)
    {
        if (ActionsEnabled == enabled)
            return;

        ActionsEnabled = enabled;
        OnPropertyChanged(nameof(ActionsEnabled));
    }

    public void MarkQuantityInvalid()
    {
        HasQuantityError = true;
        OnPropertyChanged(nameof(HasQuantityError));
    }

    private void ClearQuantityError()
    {
        if (!HasQuantityError)
            return;

        HasQuantityError = false;
        OnPropertyChanged(nameof(HasQuantityError));
    }

    private static string Format(double? value) => value?.ToString("0.###") ?? "—";

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

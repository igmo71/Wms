using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Wms.Contracts.Mobile.V1;

namespace Wms.Mobile;

public sealed class ReceivingOrderLineViewState : INotifyPropertyChanged
{
    private string _quantityText = string.Empty;

    public int LineNumber { get; private set; }
    public Guid StockKeepingUnitId { get; private set; }
    public string SkuCode { get; private set; } = string.Empty;
    public string SkuName { get; private set; } = string.Empty;
    public string? UnitOfMeasure { get; private set; }
    public decimal PlanQuantity { get; private set; }
    public decimal? FactQuantity { get; private set; }
    public decimal? DifferenceQuantity { get; private set; }
    public string? Comment { get; private set; }
    public bool CanEdit { get; private set; }
    public bool IsEditing { get; private set; }
    public bool IsAccented { get; private set; }
    public string? AccentText { get; private set; }
    public bool HasQuantityError { get; private set; }
    public bool ActionsEnabled { get; private set; }

    public string QuantitySummary =>
        $"План: {Format(PlanQuantity)} · Факт: {FormatFact(FactQuantity)} · "
        + $"Откл.: {FormatDifference(DifferenceQuantity)} {UnitOfMeasure}";
    public string LineText => $"Строка {LineNumber} · Код: {SkuCode}";
    public bool HasComment => !string.IsNullOrWhiteSpace(Comment);
    public bool HasAccentText => !string.IsNullOrEmpty(AccentText);
    public bool ShowActions => CanEdit && !IsEditing;

    public string QuantityText
    {
        get => _quantityText;
        set
        {
            if (_quantityText == value)
            {
                return;
            }

            var hadError = HasQuantityError;
            _quantityText = value;
            HasQuantityError = false;
            OnPropertyChanged();
            if (hadError)
            {
                OnPropertyChanged(nameof(HasQuantityError));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public static ReceivingOrderLineViewState From(
        MobileReceivingOrderLineResponse line,
        bool canEdit)
    {
        var state = new ReceivingOrderLineViewState();
        state.Update(line, canEdit);
        return state;
    }

    public void Update(MobileReceivingOrderLineResponse line, bool canEdit)
    {
        LineNumber = line.LineNumber;
        StockKeepingUnitId = line.StockKeepingUnitId;
        SkuCode = line.SkuCode;
        SkuName = line.SkuName;
        UnitOfMeasure = line.UnitOfMeasure;
        PlanQuantity = line.PlanQuantity;
        FactQuantity = line.FactQuantity;
        DifferenceQuantity = line.DifferenceQuantity;
        Comment = line.Comment;
        CanEdit = canEdit;
        OnPropertyChanged(string.Empty);
    }

    public void BeginEditing()
    {
        QuantityText = FactQuantity?.ToString("0.###", CultureInfo.InvariantCulture)
            ?? string.Empty;
        IsEditing = true;
        ClearQuantityError();
        OnPropertyChanged(nameof(IsEditing));
        OnPropertyChanged(nameof(ShowActions));
    }

    public void EndEditing()
    {
        IsEditing = false;
        ClearQuantityError();
        OnPropertyChanged(nameof(IsEditing));
        OnPropertyChanged(nameof(ShowActions));
    }

    public void SetAccent(bool isAccented, string? text)
    {
        IsAccented = isAccented;
        AccentText = isAccented ? text : null;
        OnPropertyChanged(nameof(IsAccented));
        OnPropertyChanged(nameof(AccentText));
        OnPropertyChanged(nameof(HasAccentText));
    }

    public void SetActionsEnabled(bool enabled)
    {
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
        {
            return;
        }

        HasQuantityError = false;
        OnPropertyChanged(nameof(HasQuantityError));
    }

    private static string Format(decimal value) => value.ToString("0.###");

    private static string FormatFact(decimal? value) =>
        value?.ToString("0.###") ?? "Не проверено";

    private static string FormatDifference(decimal? value) => value switch
    {
        null => "—",
        > 0 => $"+{value.Value:0.###}",
        _ => value.Value.ToString("0.###")
    };

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

using System.ComponentModel;
using System.Runtime.CompilerServices;
using Wms.Contracts.Mobile.V1;

namespace Wms.Mobile;

public sealed class ShippingOrderPickingLineViewState : INotifyPropertyChanged
{
    public int LineNumber { get; private set; }
    public string SkuCode { get; private set; } = string.Empty;
    public string SkuName { get; private set; } = string.Empty;
    public string? UnitOfMeasure { get; private set; }
    public decimal PlanQuantity { get; private set; }
    public decimal FactQuantity { get; private set; }
    public decimal RemainingQuantity { get; private set; }
    public string? Comment { get; private set; }
    public bool ActionsEnabled { get; private set; }
    public bool IsAccented { get; private set; }
    public IReadOnlyList<ShippingOrderPickingMovementViewState> Movements { get; private set; } = [];

    public string LineText => $"Строка {LineNumber} · Код: {SkuCode}";
    public string QuantitySummary =>
        $"План: {PlanQuantity:0.###} · Факт: {FactQuantity:0.###} · "
        + $"Осталось: {RemainingQuantity:0.###} {UnitOfMeasure}";
    public bool CanAddMovement => ActionsEnabled && RemainingQuantity > 0;
    public bool HasMovements => Movements.Count > 0;
    public bool HasComment => !string.IsNullOrWhiteSpace(Comment);

    public event PropertyChangedEventHandler? PropertyChanged;

    public static ShippingOrderPickingLineViewState From(
        MobileShippingOrderLineResponse line,
        IEnumerable<MobileShippingOrderMovementResponse> movements,
        bool actionsEnabled,
        int? accentedLineNumber,
        Guid? accentedMovementId)
    {
        var state = new ShippingOrderPickingLineViewState();
        state.Update(
            line,
            movements,
            actionsEnabled,
            accentedLineNumber,
            accentedMovementId);
        return state;
    }

    public void Update(
        MobileShippingOrderLineResponse line,
        IEnumerable<MobileShippingOrderMovementResponse> movements,
        bool actionsEnabled,
        int? accentedLineNumber,
        Guid? accentedMovementId)
    {
        LineNumber = line.LineNumber;
        SkuCode = line.SkuCode;
        SkuName = line.SkuName;
        UnitOfMeasure = line.UnitOfMeasure;
        PlanQuantity = line.PlanQuantity;
        FactQuantity = line.FactQuantity;
        RemainingQuantity = line.RemainingQuantity;
        Comment = line.Comment;
        ActionsEnabled = actionsEnabled;
        IsAccented = line.LineNumber == accentedLineNumber;
        Movements = movements
            .OrderBy(x => x.CreatedAtUtc)
            .ThenBy(x => x.Id)
            .Select(x => ShippingOrderPickingMovementViewState.From(
                x,
                actionsEnabled,
                x.Id == accentedMovementId))
            .ToList();
        OnPropertyChanged(string.Empty);
    }

    public void SetActionAvailability(bool canAddMovement, Guid? retryDeleteMovementId)
    {
        ActionsEnabled = canAddMovement;
        foreach (var movement in Movements)
        {
            movement.SetActionsEnabled(
                canAddMovement || movement.Id == retryDeleteMovementId);
        }

        OnPropertyChanged(nameof(ActionsEnabled));
        OnPropertyChanged(nameof(CanAddMovement));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class ShippingOrderPickingMovementViewState : INotifyPropertyChanged
{
    public Guid Id { get; private set; }
    public int LineNumber { get; private set; }
    public string SourceText { get; private set; } = string.Empty;
    public string QuantityText { get; private set; } = string.Empty;
    public bool ActionsEnabled { get; private set; }
    public bool IsAccented { get; private set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public static ShippingOrderPickingMovementViewState From(
        MobileShippingOrderMovementResponse movement,
        bool actionsEnabled,
        bool isAccented) => new()
    {
        Id = movement.Id,
        LineNumber = movement.LineNumber,
        SourceText = $"{movement.Source.Address} · {movement.Source.Name}",
        QuantityText = $"Количество: {movement.Quantity:0.###}",
        ActionsEnabled = actionsEnabled,
        IsAccented = isAccented
    };

    public void SetActionsEnabled(bool enabled)
    {
        ActionsEnabled = enabled;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ActionsEnabled)));
    }
}

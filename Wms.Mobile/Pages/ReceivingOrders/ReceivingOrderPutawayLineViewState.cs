using System.ComponentModel;
using System.Runtime.CompilerServices;
using Wms.Contracts.Mobile.V1;

namespace Wms.Mobile;

public sealed class ReceivingOrderPutawayLineViewState : INotifyPropertyChanged
{
    public int LineNumber { get; private set; }
    public string SkuCode { get; private set; } = string.Empty;
    public string SkuName { get; private set; } = string.Empty;
    public string? UnitOfMeasure { get; private set; }
    public double FactQuantity { get; private set; }
    public double AllocatedQuantity { get; private set; }
    public double RemainingQuantity { get; private set; }
    public bool ActionsEnabled { get; private set; }
    public IReadOnlyList<ReceivingOrderPutawayMovementViewState> Movements { get; private set; } = [];

    public string QuantitySummary =>
        $"Принято: {FactQuantity:0.###} · Размещено: {AllocatedQuantity:0.###} · "
        + $"Осталось: {RemainingQuantity:0.###} {UnitOfMeasure}";
    public string LineText => $"Строка {LineNumber} · Код: {SkuCode}";
    public bool CanAddMovement => ActionsEnabled && RemainingQuantity > 0;
    public bool HasMovements => Movements.Count > 0;

    public event PropertyChangedEventHandler? PropertyChanged;

    public static ReceivingOrderPutawayLineViewState From(
        MobileReceivingOrderLineResponse line,
        IEnumerable<MobileReceivingOrderMovementResponse> movements,
        bool actionsEnabled,
        Guid? accentedMovementId)
    {
        var state = new ReceivingOrderPutawayLineViewState();
        state.Update(line, movements, actionsEnabled, accentedMovementId);
        return state;
    }

    public void Update(
        MobileReceivingOrderLineResponse line,
        IEnumerable<MobileReceivingOrderMovementResponse> movements,
        bool actionsEnabled,
        Guid? accentedMovementId)
    {
        LineNumber = line.LineNumber;
        SkuCode = line.SkuCode;
        SkuName = line.SkuName;
        UnitOfMeasure = line.UnitOfMeasure;
        FactQuantity = line.FactQuantity ?? 0;
        AllocatedQuantity = line.AllocatedQuantity;
        RemainingQuantity = line.RemainingPutawayQuantity ?? 0;
        ActionsEnabled = actionsEnabled;
        Movements = movements
            .OrderBy(x => x.CreatedAtUtc)
            .ThenBy(x => x.Id)
            .Select(x => ReceivingOrderPutawayMovementViewState.From(
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

public sealed class ReceivingOrderPutawayMovementViewState : INotifyPropertyChanged
{
    public Guid Id { get; private set; }
    public string DestinationText { get; private set; } = string.Empty;
    public string QuantityText { get; private set; } = string.Empty;
    public bool ActionsEnabled { get; private set; }
    public bool IsAccented { get; private set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public static ReceivingOrderPutawayMovementViewState From(
        MobileReceivingOrderMovementResponse movement,
        bool actionsEnabled,
        bool isAccented) => new()
    {
        Id = movement.Id,
        DestinationText = $"{movement.Destination.Address} · {movement.Destination.Name}",
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

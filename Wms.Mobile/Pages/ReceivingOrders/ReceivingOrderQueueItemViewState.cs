using Wms.Contracts.Mobile.V1;

namespace Wms.Mobile;

public sealed class ReceivingOrderQueueItemViewState
{
    public required Guid Id { get; init; }
    public required string Number { get; init; }
    public required string DateText { get; init; }
    public required string StatusText { get; init; }
    public required string DetailsText { get; init; }
    public required string ProgressText { get; init; }
    public required bool HasSynchronizationIssue { get; init; }
    public required string SynchronizationText { get; init; }

    public static ReceivingOrderQueueItemViewState ForReceiving(
        MobileReceivingOrderSummaryResponse order) => new()
    {
        Id = order.Id,
        Number = order.Number,
        DateText = order.Date.ToString("dd.MM.yyyy HH:mm"),
        StatusText = order.Status switch
        {
            MobileReceivingOrderStatus.ReadyForReceiving => "Готов к приёмке",
            MobileReceivingOrderStatus.InReceiving => "В приёмке",
            MobileReceivingOrderStatus.ProcessingRequired => "Требуется обработка",
            _ => "Приёмка"
        },
        DetailsText = BuildReceivingDetails(order),
        HasSynchronizationIssue = !OrderSynchronizationPresentation.IsSynchronized(order.Synchronization),
        SynchronizationText = OrderSynchronizationPresentation.BuildTitle(order.Synchronization),
        ProgressText = $"Факт: {order.Progress.FactQuantity:g} из {order.Progress.PlanQuantity:g} · "
            + $"Проверено строк: {order.Progress.ConfirmedLineCount} из {order.Progress.TotalLineCount}"
    };

    public static ReceivingOrderQueueItemViewState ForPutaway(
        MobileReceivingOrderSummaryResponse order) => new()
    {
        Id = order.Id,
        Number = order.Number,
        DateText = order.Date.ToString("dd.MM.yyyy HH:mm"),
        StatusText = order.PutawayStatus == MobilePutawayStatus.Pending
            ? "Ожидает размещения"
            : "В размещении",
        DetailsText = order.ReceivingLocation is null
            ? "Позиция приёмки не указана"
            : $"Позиция приёмки: {order.ReceivingLocation.Address}",
        HasSynchronizationIssue = !OrderSynchronizationPresentation.IsSynchronized(order.Synchronization),
        SynchronizationText = OrderSynchronizationPresentation.BuildTitle(order.Synchronization),
        ProgressText = $"Размещено: {order.Progress.AllocatedQuantity:g} из {order.Progress.FactQuantity:g} · "
            + $"Строк: {order.Progress.FullyAllocatedLineCount} из {order.Progress.PositiveLineCount}"
    };

    private static string BuildReceivingDetails(MobileReceivingOrderSummaryResponse order)
    {
        var priority = string.IsNullOrWhiteSpace(order.Queue)
            ? string.Empty
            : $" · {order.Queue}";
        var location = order.ReceivingLocation is null
            ? string.Empty
            : $"\nПозиция: {order.ReceivingLocation.Address}";
        return $"{order.ShipperName}{priority}{location}";
    }
}

using Wms.Contracts.Mobile.V1;

namespace Wms.Mobile;

public sealed class ReceivingOrderQueueItemViewState
{
    public required Guid Id { get; init; }
    public required string Number { get; init; }
    public required string DateText { get; init; }
    public required string StatusText { get; init; }
    public required string ProgressText { get; init; }
    public required bool HasSynchronizationIssue { get; init; }
    public required string SynchronizationText { get; init; }
    public required bool HasJoinBlock { get; init; }
    public required string JoinBlockText { get; init; }

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
        HasSynchronizationIssue = OrderSynchronizationPresentation.HasIssue(order.Synchronization),
        SynchronizationText = OrderSynchronizationPresentation.BuildTitle(order.Synchronization),
        HasJoinBlock = !order.IsParticipant && !order.CanJoin,
        JoinBlockText = order.JoinBlockedReason ?? string.Empty,
        ProgressText = $"Проверено строк: {order.Progress.ConfirmedLineCount} из {order.Progress.TotalLineCount}"
    };
}

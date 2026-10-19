using Wms.Common;

namespace Wms.Domain;

public sealed class ReceivingOrderParticipant
{
    private ReceivingOrderParticipant() { }

    public Guid Id { get; private set; }
    public Guid ReceivingOrderId { get; private set; }
    public ReceivingOrder? ReceivingOrder { get; private set; }
    public Guid ReceivingCycleId { get; private set; }
    public ReceivingCycle? ReceivingCycle { get; private set; }
    public string UserId { get; private set; } = null!;
    public DateTimeOffset JoinedAtUtc { get; private set; }

    public static OperationResult<ReceivingOrderParticipant> CreateForCycle(
        ReceivingCycle cycle, string userId, DateTimeOffset joinedAtUtc)
    {
        if (cycle is null || cycle.Id == Guid.Empty)
            return OperationError.Invalid("Цикл приёмки обязателен.");
        if (string.IsNullOrWhiteSpace(userId))
            return OperationError.Invalid("Пользователь, присоединяющийся к ордеру, обязателен.");
        if (joinedAtUtc == default)
            return OperationError.Invalid("Время присоединения к ордеру обязательно.");

        return new ReceivingOrderParticipant
        {
            Id = Guid.NewGuid(),
            ReceivingOrderId = cycle.ReceivingOrderId,
            ReceivingCycleId = cycle.Id,
            UserId = userId.Trim(),
            JoinedAtUtc = joinedAtUtc
        };
    }

}

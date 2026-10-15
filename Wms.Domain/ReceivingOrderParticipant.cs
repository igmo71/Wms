using Wms.Common;

namespace Wms.Domain;

public sealed class ReceivingOrderParticipant
{
    private ReceivingOrderParticipant() { }

    public Guid Id { get; private set; }
    public Guid ReceivingOrderId { get; private set; }
    public ReceivingOrder? ReceivingOrder { get; private set; }
    public Guid? ReceivingCycleId { get; private set; }
    public ReceivingCycle? ReceivingCycle { get; private set; }
    public string UserId { get; private set; } = null!;
    public DateTimeOffset JoinedAtUtc { get; private set; }

    internal static OperationResult<ReceivingOrderParticipant> Create(
        Guid receivingOrderId,
        string userId,
        DateTimeOffset joinedAtUtc)
    {
        if (receivingOrderId == Guid.Empty)
            return OperationError.Invalid("Идентификатор приходного ордера обязателен.");
        if (string.IsNullOrWhiteSpace(userId))
            return OperationError.Invalid("Пользователь, присоединяющийся к ордеру, обязателен.");
        if (joinedAtUtc == default)
            return OperationError.Invalid("Время присоединения к ордеру обязательно.");

        return new ReceivingOrderParticipant
        {
            Id = Guid.NewGuid(),
            ReceivingOrderId = receivingOrderId,
            UserId = userId.Trim(),
            JoinedAtUtc = joinedAtUtc
        };
    }

    public static OperationResult<ReceivingOrderParticipant> CreateForCycle(
        ReceivingCycle cycle, string userId, DateTimeOffset joinedAtUtc)
    {
        if (cycle is null || cycle.Id == Guid.Empty)
            return OperationError.Invalid("Цикл приёмки обязателен.");

        var result = Create(cycle.ReceivingOrderId, userId, joinedAtUtc);
        if (result.IsSuccess)
            result.Value!.ReceivingCycleId = cycle.Id;
        return result;
    }
}

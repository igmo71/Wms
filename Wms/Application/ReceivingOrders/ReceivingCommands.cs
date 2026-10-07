namespace Wms.Application.ReceivingOrders;

public sealed record StartReceivingCommand(Guid OrderId, Guid ReceivingLocationId);
public sealed record JoinReceivingOrderCommand(Guid OrderId, Guid? ReceivingLocationId);

public sealed record CompleteReceivingCommand(
    Guid OrderId,
    Guid? ReceivingLocationId,
    string ExpectedFingerprint,
    string? ManagerReason);

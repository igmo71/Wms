namespace Wms.Application.ReceivingOrders;

public sealed record StartReceivingCommand(Guid OrderId, Guid ReceivingLocationId);
public sealed record JoinReceivingOrderCommand(Guid OrderId, Guid? ReceivingLocationId);

public sealed record CompleteReceivingCommand(Guid OrderId, Guid? ReceivingLocationId);

public sealed record IncrementReceivingFactCommand(Guid OrderId, int LineNumber);
public sealed record SetReceivingFactCommand(Guid OrderId, int LineNumber, decimal FactQuantity);
public sealed record SetReceivingItemCommentCommand(Guid OrderId, int LineNumber, string? Comment);

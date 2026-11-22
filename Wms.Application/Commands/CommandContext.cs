namespace Wms.Application.Commands;

public sealed record CommandContext(Guid RequestId, string UserId);

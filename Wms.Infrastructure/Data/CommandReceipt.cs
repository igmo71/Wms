using Wms.Domain;

namespace Wms.Data;

public sealed class CommandReceipt
{
    private readonly List<WarehouseAuditEvent> _auditEvents = [];

    public string UserId { get; set; } = null!;
    public string CommandType { get; set; } = null!;
    public Guid RequestId { get; set; }
    public string RequestHash { get; set; } = null!;
    public Guid ResultResourceId { get; set; }
    public DateTimeOffset CompletedAtUtc { get; set; }
    public IReadOnlyCollection<WarehouseAuditEvent> AuditEvents => _auditEvents;
}

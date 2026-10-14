namespace Wms.Data;

public sealed class CommandReceipt
{
    public string UserId { get; set; } = null!;
    public string CommandType { get; set; } = null!;
    public Guid RequestId { get; set; }
    public string RequestHash { get; set; } = null!;
    public Guid ResultResourceId { get; set; }
    public DateTimeOffset CompletedAtUtc { get; set; }
}

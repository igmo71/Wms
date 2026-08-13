using Wms.Domain.Enums;

namespace Wms.Domain;

public class InventoryCount
{
    public Guid Id { get; set; }

    public string Number { get; set; } = string.Empty;
    public DateTime Date { get; set; }

    public Guid WarehouseId { get; set; }
    public Warehouse? Warehouse { get; set; }

    public InventoryCountStatus Status { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTimeOffset? PostedAtUtc { get; set; }
    public string? PostedBy { get; set; }

    public List<InventoryCountItem> Items { get; set; } = [];
}

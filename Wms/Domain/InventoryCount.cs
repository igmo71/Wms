using Wms.Domain.Enums;

namespace Wms.Domain;

public class InventoryCount
{
    public Guid Id { get; set; }

    public Guid WarehouseId { get; set; }
    public Warehouse? Warehouse { get; set; }

    public InventoryCountStatus Status { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? UpdatedAtUtc { get; set; }
    public DateTimeOffset? PostedAtUtc { get; set; }

    public List<InventoryCountItem> Items { get; set; } = [];
}

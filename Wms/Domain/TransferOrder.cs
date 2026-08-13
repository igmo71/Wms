using Wms.Domain.Enums;

namespace Wms.Domain;

public class TransferOrder
{
    public Guid Id { get; set; }
    public string Number { get; set; } = string.Empty;
    public DateTime Date { get; set; }

    public Guid WarehouseId { get; set; }
    public Warehouse? Warehouse { get; set; }

    public Guid? TransitStorageLocationId { get; set; }
    public StorageLocation? TransitStorageLocation { get; set; }

    public TransferOrderStatus Status { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? UpdatedAtUtc { get; set; }
    public DateTimeOffset? StartedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }

    public string CreatedBy { get; set; } = string.Empty;
    public string? StartedBy { get; set; }
    public string? CompletedBy { get; set; }

    public byte[] RowVersion { get; set; } = [];
}

using Wms.Domain.Enums;

namespace Wms.Domain;

public class InventoryMovement
{
    public Guid Id { get; set; }

    public Guid WarehouseId { get; set; }
    public Warehouse? Warehouse { get; set; }

    public Guid? SourceStorageLocationId { get; set; }
    public StorageLocation? SourceStorageLocation { get; set; }

    public Guid? DestinationStorageLocationId { get; set; }
    public StorageLocation? DestinationStorageLocation { get; set; }

    public Guid StockKeepingUnitId { get; set; }
    public StockKeepingUnit? StockKeepingUnit { get; set; }

    public double Quantity { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public DateTimeOffset? PostedAtUtc { get; set; }


    public Guid? RecorderId { get; set; }
    public int? RecorderLineNumber { get; set; }
    public RecorderType RecorderType { get; set; }
}

using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Application.Services.Inventory;

public class InventoryMovementListItem
{
    public InventoryMovement Movement { get; init; } = null!;
    public string? RecorderNumber { get; init; }
    public DateTime? RecorderDate { get; init; }

    public DateTimeOffset? PostedAtUtc => Movement.PostedAtUtc;
    public Warehouse? Warehouse => Movement.Warehouse;
    public StorageLocation? SourceStorageLocation => Movement.SourceStorageLocation;
    public StorageLocation? DestinationStorageLocation => Movement.DestinationStorageLocation;
    public StockKeepingUnit? StockKeepingUnit => Movement.StockKeepingUnit;
    public double Quantity => Movement.Quantity;
    public RecorderType RecorderType => Movement.RecorderType;
    public Guid? RecorderId => Movement.RecorderId;
}

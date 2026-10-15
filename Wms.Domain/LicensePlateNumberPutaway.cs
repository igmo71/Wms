using Wms.Common;

namespace Wms.Domain;

public sealed class LicensePlateNumberPutaway
{
    private LicensePlateNumberPutaway() { }

    public Guid Id { get; private set; }
    public Guid LicensePlateNumberId { get; private set; }
    public LicensePlateNumber LicensePlateNumber { get; private set; } = null!;
    public Guid LicensePlateNumberItemId { get; private set; }
    public LicensePlateNumberItem LicensePlateNumberItem { get; private set; } = null!;
    public Guid StockKeepingUnitId { get; private set; }
    public StockKeepingUnit StockKeepingUnit { get; private set; } = null!;
    public DateOnly? ExpirationDate { get; private set; }
    public decimal Quantity { get; private set; }
    public Guid SourceStorageLocationId { get; private set; }
    public StorageLocation SourceStorageLocation { get; private set; } = null!;
    public Guid DestinationStorageLocationId { get; private set; }
    public StorageLocation DestinationStorageLocation { get; private set; } = null!;
    public Guid InventoryMovementId { get; private set; }
    public InventoryMovement InventoryMovement { get; private set; } = null!;
    public string CreatedBy { get; private set; } = null!;
    public DateTimeOffset CreatedAtUtc { get; private set; }

    internal static OperationResult<LicensePlateNumberPutaway> Create(
        Guid id,
        Guid licensePlateNumberId,
        Guid licensePlateNumberItemId,
        Guid stockKeepingUnitId,
        DateOnly? expirationDate,
        Guid sourceStorageLocationId,
        Guid destinationStorageLocationId,
        decimal quantity,
        Guid inventoryMovementId,
        string createdBy,
        DateTimeOffset createdAtUtc)
    {
        if (id == Guid.Empty || licensePlateNumberId == Guid.Empty
            || licensePlateNumberItemId == Guid.Empty || stockKeepingUnitId == Guid.Empty
            || sourceStorageLocationId == Guid.Empty || destinationStorageLocationId == Guid.Empty
            || inventoryMovementId == Guid.Empty)
            return OperationError.Invalid("Идентификаторы размещения LPN обязательны.");
        if (!WarehouseQuantity.IsPositive(quantity))
            return OperationError.Invalid("Количество размещения должно быть больше нуля.");
        if (string.IsNullOrWhiteSpace(createdBy) || createdAtUtc == default)
            return OperationError.Invalid("Для размещения обязательны пользователь и время.");

        return new LicensePlateNumberPutaway
        {
            Id = id,
            LicensePlateNumberId = licensePlateNumberId,
            LicensePlateNumberItemId = licensePlateNumberItemId,
            StockKeepingUnitId = stockKeepingUnitId,
            ExpirationDate = expirationDate,
            SourceStorageLocationId = sourceStorageLocationId,
            DestinationStorageLocationId = destinationStorageLocationId,
            Quantity = quantity,
            InventoryMovementId = inventoryMovementId,
            CreatedBy = createdBy.Trim(),
            CreatedAtUtc = createdAtUtc
        };
    }
}

using Wms.Common;

namespace Wms.Domain;

public sealed class ReplenishmentTransfer
{
    private ReplenishmentTransfer() { }

    public Guid Id { get; private set; }
    public Guid ReplenishmentSourceCycleId { get; private set; }
    public Guid DestinationStorageLocationId { get; private set; }
    public DateOnly? ExpirationDate { get; private set; }
    public decimal Quantity { get; private set; }
    public Guid InventoryMovementId { get; private set; }
    public string ConfirmedBy { get; private set; } = null!;
    public DateTimeOffset ConfirmedAtUtc { get; private set; }

    internal static OperationResult<ReplenishmentTransfer> Create(
        Guid id, Guid cycleId, Guid destinationId, DateOnly? expirationDate,
        decimal quantity, Guid movementId, string userId, DateTimeOffset confirmedAtUtc)
    {
        if (id == Guid.Empty || cycleId == Guid.Empty || destinationId == Guid.Empty || movementId == Guid.Empty)
            return OperationError.Invalid("Идентификаторы шага Подпитки обязательны.");
        if (!WarehouseQuantity.IsPositive(quantity))
            return OperationError.Invalid("Количество Подпитки должно быть положительным с точностью до 0,001.");
        if (string.IsNullOrWhiteSpace(userId) || userId.Length > 36 || confirmedAtUtc == default)
            return OperationError.Invalid("Для шага Подпитки обязательны пользователь и время.");

        return new ReplenishmentTransfer
        {
            Id = id,
            ReplenishmentSourceCycleId = cycleId,
            DestinationStorageLocationId = destinationId,
            ExpirationDate = expirationDate,
            Quantity = quantity,
            InventoryMovementId = movementId,
            ConfirmedBy = userId.Trim(),
            ConfirmedAtUtc = confirmedAtUtc
        };
    }
}

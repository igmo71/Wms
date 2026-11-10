using Wms.Common;

namespace Wms.Domain;

public sealed class PickingReservation
{
    private PickingReservation() { }

    public Guid PickingEntryId { get; private set; }
    public Guid PickingAssemblyId { get; private set; }
    public Guid WarehouseId { get; private set; }
    public Guid StorageLocationId { get; private set; }
    public Guid StockKeepingUnitId { get; private set; }
    public DateOnly? ExpirationDate { get; private set; }
    public decimal Quantity { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? UpdatedAtUtc { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }
    public string CreatedBy { get; private set; } = null!;
    public byte[] RowVersion { get; private set; } = [];

    public static OperationResult<PickingReservation> Create(
        Guid pickingEntryId, Guid pickingAssemblyId, Guid warehouseId,
        Guid storageLocationId, Guid stockKeepingUnitId, DateOnly? expirationDate,
        decimal quantity, string userId, DateTimeOffset createdAtUtc)
    {
        if (pickingEntryId == Guid.Empty || pickingAssemblyId == Guid.Empty
            || warehouseId == Guid.Empty || storageLocationId == Guid.Empty
            || stockKeepingUnitId == Guid.Empty)
            return OperationError.Invalid("Идентификаторы резерва Отбора обязательны.");
        if (!WarehouseQuantity.IsPositive(quantity))
            return OperationError.Invalid("Количество резерва должно быть положительным с точностью до 0,001.");
        if (string.IsNullOrWhiteSpace(userId) || userId.Length > 36 || createdAtUtc == default)
            return OperationError.Invalid("Для резерва обязательны пользователь и время.");

        return new PickingReservation
        {
            PickingEntryId = pickingEntryId,
            PickingAssemblyId = pickingAssemblyId,
            WarehouseId = warehouseId,
            StorageLocationId = storageLocationId,
            StockKeepingUnitId = stockKeepingUnitId,
            ExpirationDate = expirationDate,
            Quantity = quantity,
            CreatedBy = userId.Trim(),
            CreatedAtUtc = createdAtUtc
        };
    }

    public OperationResult Reduce(decimal quantity, DateTimeOffset changedAtUtc)
    {
        if (CompletedAtUtc is not null)
            return OperationError.Conflict("Резерв уже завершён.");
        if (!WarehouseQuantity.IsPositive(quantity) || quantity > Quantity)
            return OperationError.Invalid("Освобождаемое количество превышает резерв или имеет неверную точность.");
        if (changedAtUtc == default || changedAtUtc < (UpdatedAtUtc ?? CreatedAtUtc))
            return OperationError.Invalid("Время изменения резерва предшествует последнему действию.");

        Quantity -= quantity;
        UpdatedAtUtc = changedAtUtc;
        if (Quantity == 0)
            CompletedAtUtc = changedAtUtc;
        return OperationResult.Success();
    }

    public OperationResult Complete(DateTimeOffset completedAtUtc) => Reduce(Quantity, completedAtUtc);
}

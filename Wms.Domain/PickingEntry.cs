using Wms.Common;

namespace Wms.Domain;

public sealed class PickingEntry
{
    private PickingEntry() { }

    public Guid Id { get; private set; }
    public Guid PickingAssemblyId { get; private set; }
    public Guid WarehouseId { get; private set; }
    public Guid StorageLocationId { get; private set; }
    public Guid StockKeepingUnitId { get; private set; }
    public DateOnly? ExpirationDate { get; private set; }
    public decimal Quantity { get; private set; }
    public long OperationalRevision { get; private set; }
    public string ConfirmedBy { get; private set; } = null!;
    public DateTimeOffset ConfirmedAtUtc { get; private set; }
    public string? LastChangedBy { get; private set; }
    public DateTimeOffset? LastChangedAtUtc { get; private set; }

    public static OperationResult<PickingEntry> Create(
        Guid assemblyId, Guid warehouseId, Guid locationId, Guid skuId,
        DateOnly? expirationDate, decimal quantity, string userId,
        DateTimeOffset confirmedAtUtc)
    {
        if (assemblyId == Guid.Empty || warehouseId == Guid.Empty
            || locationId == Guid.Empty || skuId == Guid.Empty
            || !WarehouseQuantity.IsPositive(quantity)
            || string.IsNullOrWhiteSpace(userId) || userId.Trim().Length > 36
            || confirmedAtUtc == default)
            return OperationError.Invalid("Запись Отбора требует ячейку, SKU, количество и исполнителя.");
        return new PickingEntry
        {
            Id = Guid.NewGuid(), PickingAssemblyId = assemblyId,
            WarehouseId = warehouseId, StorageLocationId = locationId,
            StockKeepingUnitId = skuId, ExpirationDate = expirationDate,
            Quantity = quantity, ConfirmedBy = userId.Trim(),
            ConfirmedAtUtc = confirmedAtUtc
        };
    }

    public OperationResult Reduce(decimal quantity, string userId,
        DateTimeOffset changedAtUtc)
    {
        if (!WarehouseQuantity.IsPositive(quantity) || quantity > Quantity
            || string.IsNullOrWhiteSpace(userId) || userId.Trim().Length > 36
            || changedAtUtc < (LastChangedAtUtc ?? ConfirmedAtUtc))
            return OperationError.Invalid("Возвращаемое количество или время недопустимо.");
        Quantity -= quantity;
        LastChangedBy = userId.Trim();
        LastChangedAtUtc = changedAtUtc;
        OperationalRevision++;
        return OperationResult.Success();
    }
}

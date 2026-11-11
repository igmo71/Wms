using Wms.Application.Authorization;
using Wms.Application.CommandExecution;
using Wms.Common;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Application.Inventory;

public readonly record struct StockGroupKey(
    Guid WarehouseId, Guid StorageLocationId, Guid StockKeepingUnitId, DateOnly? ExpirationDate);

public sealed record StockReservationView(
    Guid PickingEntryId, Guid PickingAssemblyId, decimal Quantity, string CreatedBy);

public sealed record StockHoldView(
    StorageLocationLockOwnerType OwnerType, Guid? OwnerId, string Reason, string LockedBy);

public sealed record StockAvailability(
    StockGroupKey Group,
    string LocationCode,
    decimal AccountingQuantity,
    IReadOnlyList<StockReservationView> Reservations,
    decimal BlockedQuantity,
    StockHoldView? Hold)
{
    public decimal ReservedQuantity => Reservations.Sum(x => x.Quantity);

    public decimal AvailableQuantity => AvailableForNewPick(null);

    public decimal AvailableForNewPick(Guid? pickingAssemblyId)
    {
        if (Hold is not null && (pickingAssemblyId is null
            || Hold.OwnerType != StorageLocationLockOwnerType.PickingAssembly
            || Hold.OwnerId != pickingAssemblyId))
            return 0;
        return Math.Max(0, AccountingQuantity - ReservedQuantity - BlockedQuantity);
    }

    public decimal ReservedFor(Guid pickingAssemblyId) =>
        Reservations.Where(x => x.PickingAssemblyId == pickingAssemblyId).Sum(x => x.Quantity);

    public decimal AvailableForReservedIssue(Guid pickingAssemblyId)
    {
        if (Hold is not null && (Hold.OwnerType != StorageLocationLockOwnerType.PickingAssembly
            || Hold.OwnerId != pickingAssemblyId))
            return 0;
        var owned = ReservedFor(pickingAssemblyId);
        return Math.Min(owned, Math.Max(0,
            AccountingQuantity - (ReservedQuantity - owned) - BlockedQuantity));
    }

    public decimal AvailableForReplenishment(Guid replenishmentTaskId)
    {
        if (Hold is null || Hold.OwnerType != StorageLocationLockOwnerType.ReplenishmentTask
            || Hold.OwnerId != replenishmentTaskId)
            return 0;
        return Math.Max(0, AccountingQuantity - ReservedQuantity - BlockedQuantity);
    }
}

public interface IStockAvailabilityStore
{
    Task<IReadOnlyList<StockAvailability>> ReadSkuAsync(
        Guid warehouseId, Guid stockKeepingUnitId, CancellationToken ct);
    Task<IReadOnlyList<StockAvailability>> ReadSkuAsync(
        ICommandExecutionContext context, Guid warehouseId,
        Guid stockKeepingUnitId, CancellationToken ct);
}

public interface IStockReservationStore
{
    Task<OperationResult> ReserveAsync(
        ICommandExecutionContext context, PickingReservation reservation, CancellationToken ct);

    Task<OperationResult> HoldAsync(
        ICommandExecutionContext context, Guid warehouseId, StorageLocationLock hold, CancellationToken ct);

    Task<OperationResult> ReduceAsync(
        ICommandExecutionContext context, Guid pickingEntryId, decimal quantity,
        DateTimeOffset changedAtUtc, CancellationToken ct);
}

public interface IStockShortageStore
{
    Task<OperationResult> ReportAsync(
        ICommandExecutionContext context, StockShortage shortage, CancellationToken ct);

    Task<StockShortageResolutionState?> LoadForResolutionAsync(
        ICommandExecutionContext context, Guid shortageId, CancellationToken ct);
}

public sealed record StockShortageResolutionState(
    StockShortage Shortage, StorageLocation Location);

public sealed class GetStockAvailability(IStockAvailabilityStore store, WarehousePermission permission)
{
    public async Task<OperationResult<IReadOnlyList<StockAvailability>>> ExecuteAsync(
        Guid warehouseId, Guid stockKeepingUnitId, string userId, CancellationToken ct = default)
    {
        var access = await permission.RequireOperatorAsync(userId, ct);
        if (!access.IsSuccess)
            return access.Error!;
        if (warehouseId == Guid.Empty || stockKeepingUnitId == Guid.Empty)
            return OperationError.Invalid("Выберите склад и SKU для просмотра остатков.");
        return OperationResult<IReadOnlyList<StockAvailability>>.Success(
            await store.ReadSkuAsync(warehouseId, stockKeepingUnitId, ct));
    }
}

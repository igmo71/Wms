using Microsoft.EntityFrameworkCore;
using Wms.Application.CommandExecution;
using Wms.Application.Inventory;
using Wms.Common;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Data;

public sealed class StockAvailabilityStore(IDbContextFactory<ApplicationDbContext> factory)
    : IStockAvailabilityStore, IStockReservationStore
{
    public async Task<IReadOnlyList<StockAvailability>> ReadSkuAsync(
        Guid warehouseId, Guid stockKeepingUnitId, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await ReadSkuAsync(db, warehouseId, stockKeepingUnitId, ct);
    }

    public async Task<OperationResult> ReserveAsync(
        ICommandExecutionContext context, PickingReservation reservation, CancellationToken ct)
    {
        var session = RequireSession(context);
        var location = await LockLocationAsync(session, reservation.WarehouseId,
            reservation.StorageLocationId, ct);
        if (location is null)
            return OperationError.NotFound("Ячейка резерва не найдена на выбранном складе.");
        if (!IsOperational(location))
            return OperationError.Conflict("Ячейка недоступна для резервирования.");
        if (reservation.CompletedAtUtc is not null || !WarehouseQuantity.IsPositive(reservation.Quantity))
            return OperationError.Invalid("Для нового резерва нужно положительное активное количество.");

        var state = await ReadSkuAsync(session.Db, reservation.WarehouseId,
            reservation.StockKeepingUnitId, ct, reservation.StorageLocationId);
        var group = state.SingleOrDefault(x => x.Group.StorageLocationId == reservation.StorageLocationId
            && x.Group.ExpirationDate == reservation.ExpirationDate);
        if (group is null || group.AvailableForNewPick(reservation.PickingAssemblyId) < reservation.Quantity)
            return OperationError.Conflict("Недостаточно доступного количества в выбранной группе срока.");
        if (session.Db.PickingReservations.Local.Any(x => x.PickingEntryId == reservation.PickingEntryId)
            || await session.Db.PickingReservations.AnyAsync(
                x => x.PickingEntryId == reservation.PickingEntryId, ct))
            return OperationError.Conflict("Резерв этой записи Отбора уже существует.");

        location.AdvanceOperationalRevision();
        context.Stage(reservation);
        return OperationResult.Success();
    }

    public async Task<OperationResult> HoldAsync(
        ICommandExecutionContext context, Guid warehouseId, StorageLocationLock hold, CancellationToken ct)
    {
        if (hold.OwnerType is not (StorageLocationLockOwnerType.PickingAssembly
            or StorageLocationLockOwnerType.ReplenishmentTask))
            return OperationError.Invalid("Удержание должно принадлежать сборке или заданию Подпитки.");
        var session = RequireSession(context);
        var location = await LockLocationAsync(session, warehouseId, hold.StorageLocationId, ct);
        if (location is null)
            return OperationError.NotFound("Ячейка удержания не найдена на выбранном складе.");
        if (!IsOperational(location) || session.Db.StorageLocationLocks.Local.Any(
                x => x.StorageLocationId == hold.StorageLocationId))
            return OperationError.Conflict("Ячейка уже заблокирована или недоступна.");

        await session.Db.PickingReservations.Where(x => x.StorageLocationId == hold.StorageLocationId
            && x.CompletedAtUtc == null).LoadAsync(ct);
        if (session.Db.PickingReservations.Local.Any(x => x.StorageLocationId == hold.StorageLocationId
            && x.CompletedAtUtc is null
            && (hold.OwnerType != StorageLocationLockOwnerType.PickingAssembly
                || x.PickingAssemblyId != hold.OwnerId)))
            return OperationError.Conflict("В ячейке есть количество, зарезервированное другой работой.");

        location.AdvanceOperationalRevision();
        context.Stage(hold);
        return OperationResult.Success();
    }

    public async Task<OperationResult> ReduceAsync(
        ICommandExecutionContext context, Guid pickingEntryId, decimal quantity,
        DateTimeOffset changedAtUtc, CancellationToken ct)
    {
        var session = RequireSession(context);
        var group = await session.Db.PickingReservations.AsNoTracking()
            .Where(x => x.PickingEntryId == pickingEntryId)
            .Select(x => new { x.WarehouseId, x.StorageLocationId }).SingleOrDefaultAsync(ct);
        if (group is null)
            return OperationError.NotFound("Резерв записи Отбора не найден.");
        var location = await LockLocationAsync(session, group.WarehouseId, group.StorageLocationId, ct);
        if (location is null)
            return OperationError.NotFound("Исходная ячейка резерва не найдена.");
        var reservation = await session.Db.PickingReservations.SingleAsync(
            x => x.PickingEntryId == pickingEntryId, ct);
        var reduced = reservation.Reduce(quantity, changedAtUtc);
        if (!reduced.IsSuccess)
            return reduced;
        location.AdvanceOperationalRevision();
        return OperationResult.Success();
    }

    private static CommandExecutionStore.Session RequireSession(ICommandExecutionContext context) =>
        context as CommandExecutionStore.Session
        ?? throw new InvalidOperationException("Резервирование должно использовать сессию команды.");

    private static bool IsOperational(StorageLocation location) =>
        !location.IsFolder && !location.DeletionMark
        && location.Warehouse is { DeletionMark: false }
        && location.Zone is { DeletionMark: false }
        && location.Zone.WarehouseId == location.WarehouseId;

    private static async Task<StorageLocation?> LockLocationAsync(
        CommandExecutionStore.Session session, Guid warehouseId, Guid locationId, CancellationToken ct)
    {
        var locations = await InventoryLocationLock.AcquireAsync(session.Db, [locationId], ct);
        return locations.TryGetValue(locationId, out var location) && location.WarehouseId == warehouseId
            ? location : null;
    }

    private static async Task<IReadOnlyList<StockAvailability>> ReadSkuAsync(
        ApplicationDbContext db, Guid warehouseId, Guid skuId, CancellationToken ct,
        Guid? locationId = null)
    {
        await db.InventoryBalances.Where(x => x.WarehouseId == warehouseId
            && x.StockKeepingUnitId == skuId
            && (locationId == null || x.StorageLocationId == locationId)).LoadAsync(ct);
        await db.PickingReservations.Where(x => x.WarehouseId == warehouseId
            && x.StockKeepingUnitId == skuId && x.CompletedAtUtc == null
            && (locationId == null || x.StorageLocationId == locationId)).LoadAsync(ct);
        await db.StockShortages.Where(x => x.WarehouseId == warehouseId
            && x.StockKeepingUnitId == skuId && x.ResolvedAtUtc == null
            && (locationId == null || x.StorageLocationId == locationId)).LoadAsync(ct);

        var balances = db.InventoryBalances.Local.Where(x => x.WarehouseId == warehouseId
            && x.StockKeepingUnitId == skuId
            && (locationId is null || x.StorageLocationId == locationId)).ToArray();
        var reservations = db.PickingReservations.Local.Where(x => x.WarehouseId == warehouseId
            && x.StockKeepingUnitId == skuId && x.CompletedAtUtc is null
            && (locationId is null || x.StorageLocationId == locationId)).ToArray();
        var shortages = db.StockShortages.Local.Where(x => x.WarehouseId == warehouseId
            && x.StockKeepingUnitId == skuId && x.ResolvedAtUtc is null
            && (locationId is null || x.StorageLocationId == locationId)).ToArray();
        var groups = balances.Select(x => new StockGroupKey(warehouseId, x.StorageLocationId, skuId, x.ExpirationDate))
            .Concat(reservations.Select(x => new StockGroupKey(warehouseId, x.StorageLocationId, skuId, x.ExpirationDate)))
            .Concat(shortages.Select(x => new StockGroupKey(warehouseId, x.StorageLocationId, skuId, x.ExpirationDate)))
            .Distinct().ToArray();
        var locationIds = groups.Select(x => x.StorageLocationId).Distinct().ToArray();
        var locations = new Dictionary<Guid, StorageLocation>();
        foreach (var batch in locationIds.Chunk(500))
        {
            foreach (var location in await db.StorageLocations.Where(x => batch.Contains(x.Id))
                .ToListAsync(ct))
                locations.Add(location.Id, location);
            await db.StorageLocationLocks.Where(x => batch.Contains(x.StorageLocationId)).LoadAsync(ct);
        }

        return groups.Select(key =>
        {
            var balance = balances.SingleOrDefault(x => x.StorageLocationId == key.StorageLocationId
                && x.ExpirationDate == key.ExpirationDate);
            var held = db.StorageLocationLocks.Local.SingleOrDefault(x => x.StorageLocationId == key.StorageLocationId);
            var reserved = reservations.Where(x => x.StorageLocationId == key.StorageLocationId
                    && x.ExpirationDate == key.ExpirationDate)
                .Select(x => new StockReservationView(x.PickingEntryId, x.PickingAssemblyId, x.Quantity, x.CreatedBy))
                .ToArray();
            var blocked = shortages.Where(x => x.StorageLocationId == key.StorageLocationId
                && x.ExpirationDate == key.ExpirationDate).Sum(x => x.BlockedQuantity);
            return new StockAvailability(key, locations[key.StorageLocationId].Code,
                balance?.Quantity ?? 0, reserved, blocked,
                held is null ? null : new StockHoldView(held.OwnerType, held.OwnerId, held.Reason, held.LockedBy));
        }).OrderBy(x => x.LocationCode, StringComparer.Ordinal)
            .ThenBy(x => x.Group.ExpirationDate is null).ThenBy(x => x.Group.ExpirationDate).ToArray();
    }
}

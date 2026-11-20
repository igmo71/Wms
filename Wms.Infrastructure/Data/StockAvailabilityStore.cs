using Microsoft.EntityFrameworkCore;
using Wms.Application.CommandExecution;
using Wms.Application.Inventory;
using Wms.Common;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Data;

public sealed class StockAvailabilityStore(IDbContextFactory<ApplicationDbContext> factory)
    : IStockAvailabilityStore, IStockReservationStore, IStockShortageStore
{
    public Task<IReadOnlyList<StockAvailability>> ReadSkuAsync(
        Guid warehouseId, Guid stockKeepingUnitId, CancellationToken ct) =>
        ReadSkusAsync(warehouseId, [stockKeepingUnitId], ct);

    public async Task<IReadOnlyList<StockAvailability>> ReadSkusAsync(
        Guid warehouseId, IReadOnlyCollection<Guid> stockKeepingUnitIds, CancellationToken ct)
    {
        var skuIds = stockKeepingUnitIds.Distinct().ToArray();
        if (skuIds.Length == 0) return [];
        await using var db = await factory.CreateDbContextAsync(ct);
        var balances = new List<BalanceRow>();
        var reservations = new List<ReservationRow>();
        var shortages = new List<ShortageRow>();
        foreach (var batch in skuIds.Chunk(500))
        {
            balances.AddRange(await db.InventoryBalances.AsNoTracking()
                .Where(x => x.WarehouseId == warehouseId && batch.Contains(x.StockKeepingUnitId))
                .Select(x => new BalanceRow(new StockGroupKey(x.WarehouseId,
                    x.StorageLocationId, x.StockKeepingUnitId, x.ExpirationDate), x.Quantity))
                .ToArrayAsync(ct));
            reservations.AddRange(await db.PickingReservations.AsNoTracking()
                .Where(x => x.WarehouseId == warehouseId && batch.Contains(x.StockKeepingUnitId)
                    && x.CompletedAtUtc == null)
                .Select(x => new ReservationRow(new StockGroupKey(x.WarehouseId,
                    x.StorageLocationId, x.StockKeepingUnitId, x.ExpirationDate),
                    new StockReservationView(x.PickingEntryId, x.PickingAssemblyId, x.Quantity, x.CreatedBy)))
                .ToArrayAsync(ct));
            shortages.AddRange(await db.StockShortages.AsNoTracking()
                .Where(x => x.WarehouseId == warehouseId && batch.Contains(x.StockKeepingUnitId)
                    && x.ResolvedAtUtc == null)
                .Select(x => new ShortageRow(new StockGroupKey(x.WarehouseId,
                    x.StorageLocationId, x.StockKeepingUnitId, x.ExpirationDate),
                    x.ExpectedQuantity - x.FoundQuantity))
                .ToArrayAsync(ct));
        }
        var locationIds = balances.Select(x => x.Group.StorageLocationId)
            .Concat(reservations.Select(x => x.Group.StorageLocationId))
            .Concat(shortages.Select(x => x.Group.StorageLocationId)).Distinct().ToArray();
        var locations = new Dictionary<Guid, LocationRow>();
        var holds = new List<HoldRow>();
        foreach (var batch in locationIds.Chunk(500))
        {
            foreach (var location in await db.StorageLocations.AsNoTracking()
                .Where(x => batch.Contains(x.Id))
                .Select(x => new LocationRow(x.Id, x.Code, x.Name)).ToArrayAsync(ct))
                locations.Add(location.Id, location);
            holds.AddRange(await db.StorageLocationLocks.AsNoTracking()
                .Where(x => batch.Contains(x.StorageLocationId))
                .Select(x => new HoldRow(x.StorageLocationId,
                    new StockHoldView(x.OwnerType, x.OwnerId, x.Reason, x.LockedBy)))
                .ToArrayAsync(ct));
        }
        return BuildAvailability(balances, reservations, shortages, locations, holds);
    }

    public Task<IReadOnlyList<StockAvailability>> ReadSkuAsync(
        ICommandExecutionContext context, Guid warehouseId,
        Guid stockKeepingUnitId, CancellationToken ct) =>
        ReadSkuAsync(RequireSession(context).Db, warehouseId, stockKeepingUnitId, ct);

    public Task<IReadOnlyList<StockAvailability>> ReadSkusAsync(
        ICommandExecutionContext context, Guid warehouseId,
        IReadOnlyCollection<Guid> stockKeepingUnitIds, CancellationToken ct) =>
        ReadSkusAsync(RequireSession(context).Db, warehouseId, stockKeepingUnitIds, ct);

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

    public async Task<OperationResult> ReportAsync(
        ICommandExecutionContext context, StockShortage shortage, CancellationToken ct)
    {
        var session = RequireSession(context);
        var location = await LockLocationAsync(session, shortage.WarehouseId,
            shortage.StorageLocationId, ct);
        if (location is null)
            return OperationError.NotFound("Ячейка сообщения о недостаче не найдена.");
        if (!IsOperational(location))
            return OperationError.Conflict("Ячейка недоступна для сообщения о недостаче.");
        var state = await ReadSkuAsync(session.Db, shortage.WarehouseId,
            shortage.StockKeepingUnitId, ct, shortage.StorageLocationId);
        var group = state.SingleOrDefault(x => x.Group.StorageLocationId == shortage.StorageLocationId
            && x.Group.ExpirationDate == shortage.ExpirationDate);
        var hold = group?.Hold;
        var ownsHold = hold is null
            || hold.OwnerId == shortage.OriginId
                && (shortage.Origin == StockShortageOrigin.Picking
                    && hold.OwnerType == StorageLocationLockOwnerType.PickingAssembly
                    || shortage.Origin == StockShortageOrigin.Replenishment
                    && hold.OwnerType == StorageLocationLockOwnerType.ReplenishmentTask);
        if (group is null || !ownsHold || group.BlockedQuantity > 0
            || group.AccountingQuantity - group.ReservedQuantity - group.BlockedQuantity
                < shortage.BlockedQuantity)
            return OperationError.Conflict("Спорная группа уже заблокирована или свободного остатка недостаточно.");
        location.AdvanceOperationalRevision();
        context.Stage(shortage);
        return OperationResult.Success();
    }

    public async Task<StockShortageResolutionState?> LoadForResolutionAsync(
        ICommandExecutionContext context, Guid shortageId, CancellationToken ct)
    {
        var session = RequireSession(context);
        await session.BeginSerializableAsync(ct);
        var group = await session.Db.StockShortages.AsNoTracking()
            .Where(x => x.Id == shortageId)
            .Select(x => new { x.WarehouseId, x.StorageLocationId })
            .SingleOrDefaultAsync(ct);
        if (group is null) return null;
        var location = await LockLocationAsync(session, group.WarehouseId,
            group.StorageLocationId, ct);
        if (location is null) return null;
        var shortage = await session.Db.StockShortages.SingleOrDefaultAsync(
            x => x.Id == shortageId, ct);
        return shortage is null ? null : new StockShortageResolutionState(shortage, location);
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

    private static Task<IReadOnlyList<StockAvailability>> ReadSkuAsync(
        ApplicationDbContext db, Guid warehouseId, Guid skuId, CancellationToken ct,
        Guid? locationId = null) =>
        ReadSkusAsync(db, warehouseId, [skuId], ct, locationId);

    private static async Task<IReadOnlyList<StockAvailability>> ReadSkusAsync(
        ApplicationDbContext db, Guid warehouseId, IReadOnlyCollection<Guid> stockKeepingUnitIds,
        CancellationToken ct, Guid? locationId = null)
    {
        var skuIds = stockKeepingUnitIds.ToHashSet();
        if (skuIds.Count == 0) return [];
        foreach (var batch in skuIds.Chunk(500))
        {
            await db.InventoryBalances.Where(x => x.WarehouseId == warehouseId
                && batch.Contains(x.StockKeepingUnitId)
                && (locationId == null || x.StorageLocationId == locationId)).LoadAsync(ct);
            await db.PickingReservations.Where(x => x.WarehouseId == warehouseId
                && batch.Contains(x.StockKeepingUnitId) && x.CompletedAtUtc == null
                && (locationId == null || x.StorageLocationId == locationId)).LoadAsync(ct);
            await db.StockShortages.Where(x => x.WarehouseId == warehouseId
                && batch.Contains(x.StockKeepingUnitId) && x.ResolvedAtUtc == null
                && (locationId == null || x.StorageLocationId == locationId)).LoadAsync(ct);
        }

        var balances = db.InventoryBalances.Local.Where(x => x.WarehouseId == warehouseId
            && skuIds.Contains(x.StockKeepingUnitId)
            && (locationId is null || x.StorageLocationId == locationId)).ToArray();
        var reservations = db.PickingReservations.Local.Where(x => x.WarehouseId == warehouseId
            && skuIds.Contains(x.StockKeepingUnitId) && x.CompletedAtUtc is null
            && (locationId is null || x.StorageLocationId == locationId)).ToArray();
        var shortages = db.StockShortages.Local.Where(x => x.WarehouseId == warehouseId
            && skuIds.Contains(x.StockKeepingUnitId) && x.ResolvedAtUtc is null
            && (locationId is null || x.StorageLocationId == locationId)).ToArray();
        var locationIds = balances.Select(x => x.StorageLocationId)
            .Concat(reservations.Select(x => x.StorageLocationId))
            .Concat(shortages.Select(x => x.StorageLocationId)).Distinct().ToArray();
        var locations = new Dictionary<Guid, StorageLocation>();
        foreach (var batch in locationIds.Chunk(500))
        {
            foreach (var location in await db.StorageLocations.Where(x => batch.Contains(x.Id))
                .ToListAsync(ct))
                locations.Add(location.Id, location);
            await db.StorageLocationLocks.Where(x => batch.Contains(x.StorageLocationId)).LoadAsync(ct);
        }

        return BuildAvailability(balances, reservations, shortages, locations,
            db.StorageLocationLocks.Local);
    }

    private static IReadOnlyList<StockAvailability> BuildAvailability(
        IEnumerable<InventoryBalance> balances, IEnumerable<PickingReservation> reservations,
        IEnumerable<StockShortage> shortages, IReadOnlyDictionary<Guid, StorageLocation> locations,
        IEnumerable<StorageLocationLock> holds) =>
        BuildAvailability(
            balances.Select(x => new BalanceRow(new StockGroupKey(x.WarehouseId,
                x.StorageLocationId, x.StockKeepingUnitId, x.ExpirationDate), x.Quantity)),
            reservations.Select(x => new ReservationRow(new StockGroupKey(x.WarehouseId,
                x.StorageLocationId, x.StockKeepingUnitId, x.ExpirationDate),
                new StockReservationView(x.PickingEntryId, x.PickingAssemblyId, x.Quantity, x.CreatedBy))),
            shortages.Select(x => new ShortageRow(new StockGroupKey(x.WarehouseId,
                x.StorageLocationId, x.StockKeepingUnitId, x.ExpirationDate), x.BlockedQuantity)),
            locations.ToDictionary(x => x.Key,
                x => new LocationRow(x.Value.Id, x.Value.Code, x.Value.Name)),
            holds.Select(x => new HoldRow(x.StorageLocationId,
                new StockHoldView(x.OwnerType, x.OwnerId, x.Reason, x.LockedBy))));

    private static IReadOnlyList<StockAvailability> BuildAvailability(
        IEnumerable<BalanceRow> balances, IEnumerable<ReservationRow> reservations,
        IEnumerable<ShortageRow> shortages, IReadOnlyDictionary<Guid, LocationRow> locations,
        IEnumerable<HoldRow> holds)
    {
        var balancesByGroup = balances.ToDictionary(x => x.Group, x => x.Quantity);
        var reservationsByGroup = reservations.ToLookup(x => x.Group);
        var shortagesByGroup = shortages.ToLookup(x => x.Group);
        var holdsByLocation = holds.ToDictionary(x => x.StorageLocationId, x => x.Hold);
        var groups = balancesByGroup.Keys.Concat(reservationsByGroup.Select(x => x.Key))
            .Concat(shortagesByGroup.Select(x => x.Key)).Distinct();
        return groups.Select(key =>
        {
            var accountingQuantity = balancesByGroup.GetValueOrDefault(key);
            var held = holdsByLocation.GetValueOrDefault(key.StorageLocationId);
            var reserved = reservationsByGroup[key].Select(x => x.Reservation).ToArray();
            var blocked = shortagesByGroup[key].Sum(x => x.BlockedQuantity);
            return new StockAvailability(key, locations[key.StorageLocationId].Code,
                accountingQuantity, reserved, blocked, held)
            { LocationName = locations[key.StorageLocationId].Name };
        }).OrderBy(x => x.LocationCode, StringComparer.Ordinal)
            .ThenBy(x => x.Group.ExpirationDate is null).ThenBy(x => x.Group.ExpirationDate).ToArray();
    }

    private readonly record struct BalanceRow(StockGroupKey Group, decimal Quantity);
    private readonly record struct ReservationRow(StockGroupKey Group, StockReservationView Reservation);
    private readonly record struct ShortageRow(StockGroupKey Group, decimal BlockedQuantity);
    private readonly record struct LocationRow(Guid Id, string Code, string? Name);
    private readonly record struct HoldRow(Guid StorageLocationId, StockHoldView Hold);
}

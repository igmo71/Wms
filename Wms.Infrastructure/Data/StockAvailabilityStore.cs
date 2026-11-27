using Microsoft.EntityFrameworkCore;
using Wms.Application.CommandExecution;
using Wms.Application.Inventory;
using Wms.Common;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Data;

public sealed record CellStockAvailability(StorageLocation Location, IReadOnlyList<CellStockGroup> Groups);

public sealed record CellStockGroup(StockKeepingUnit Sku, StockAvailability Stock);

public sealed class StockAvailabilityStore(IDbContextFactory<ApplicationDbContext> factory)
    : IStockAvailabilityStore, IStockReservationStore, IStockShortageStore, ICellStockCorrectionStore
{
    public async Task<IReadOnlyList<StorageLocation>> SearchStorageCellsAsync(Guid warehouseId,
        string? searchText, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var locations = db.StorageLocations.AsNoTracking().Include(x => x.Zone)
            .Where(x => x.WarehouseId == warehouseId && !x.DeletionMark && !x.IsFolder
                && !x.Warehouse!.DeletionMark && !x.Zone!.DeletionMark
                && (x.Zone.Type == ZoneType.Storage || x.Zone.Type == ZoneType.Reserve));
        if (!string.IsNullOrWhiteSpace(searchText))
        {
            var term = searchText.Trim();
            var normalized = StorageLocationAddress.Normalize(term);
            var code = normalized.IsSuccess ? normalized.Value! : term;
            locations = locations.Where(x => x.Name.Contains(term) || x.Code.Contains(code));
        }
        return await locations.OrderBy(x => x.Code).Take(10).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<StockKeepingUnit>> SearchCorrectionSkusAsync(string? searchText, CancellationToken ct)
    {
        var term = searchText?.Trim();
        if (term is null || term.Length < 2)
            return [];

        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.StockKeepingUnits.AsNoTracking().Include(x => x.BaseUnitOfMeasure)
            .Where(x => !x.DeletionMark && ((x.Name != null && x.Name.Contains(term))
                || (x.Code != null && x.Code.Contains(term))
                || db.SkuBarcodes.Any(barcode => barcode.SkuId == x.Id && barcode.Value == term)))
            .OrderByDescending(x => x.Code == term
                || db.SkuBarcodes.Any(barcode => barcode.SkuId == x.Id && barcode.Value == term))
            .ThenBy(x => x.Name).ThenBy(x => x.Code).ThenBy(x => x.Id).Take(10).ToListAsync(ct);
    }

    public async Task<OperationResult<CellStockAvailability>> ReadCorrectionCellAsync(
        Guid warehouseId, string barcode, CancellationToken ct)
    {
        if (warehouseId == Guid.Empty)
            return OperationError.Invalid("Выберите склад.");
        var address = StorageLocationAddress.Normalize(barcode);
        if (!address.IsSuccess)
            return address.Error!;

        await using var db = await factory.CreateDbContextAsync(ct);
        var location = await db.StorageLocations.AsNoTracking()
            .Include(x => x.Warehouse).Include(x => x.Zone).Include(x => x.ActiveLock)
            .SingleOrDefaultAsync(x => x.WarehouseId == warehouseId && x.Code == address.Value, ct);
        if (location is null)
            return OperationError.NotFound("Ячейка не найдена на выбранном складе.");
        if (location.IsFolder || location.DeletionMark || location.Warehouse is not { DeletionMark: false }
            || location.Zone is not { DeletionMark: false, Type: ZoneType.Storage or ZoneType.Reserve }
            || location.Zone.WarehouseId != warehouseId)
            return OperationError.Conflict("Корректировка проводится в активной ячейке Комплектации или Хранения.");

        var locationId = location.Id;
        var skuIds = await db.InventoryBalances
            .Where(x => x.WarehouseId == warehouseId && x.StorageLocationId == locationId && x.Quantity != 0)
            .Select(x => x.StockKeepingUnitId)
            .Union(db.PickingReservations.Where(x => x.WarehouseId == warehouseId
                && x.StorageLocationId == locationId && x.CompletedAtUtc == null).Select(x => x.StockKeepingUnitId))
            .Union(db.PickingRouteReservations.Where(x => x.WarehouseId == warehouseId
                && x.StorageLocationId == locationId).Select(x => x.StockKeepingUnitId))
            .Union(db.StockShortages.Where(x => x.WarehouseId == warehouseId
                && x.StorageLocationId == locationId && x.ResolvedAtUtc == null).Select(x => x.StockKeepingUnitId))
            .ToArrayAsync(ct);
        var stock = await ReadSkusAsync(db, warehouseId, skuIds, ct, locationId);
        var skus = new Dictionary<Guid, StockKeepingUnit>();
        foreach (var batch in skuIds.Chunk(500))
        {
            var matches = await db.StockKeepingUnits.AsNoTracking().Include(x => x.BaseUnitOfMeasure)
                .Where(x => batch.Contains(x.Id)).ToArrayAsync(ct);
            foreach (var sku in matches)
                skus.Add(sku.Id, sku);
        }

        var groups = stock.Where(x => x.AccountingQuantity != 0 || x.ReservedQuantity != 0 || x.BlockedQuantity != 0)
            .Select(x => new CellStockGroup(skus[x.Group.StockKeepingUnitId], x))
            .OrderBy(x => x.Sku.Name).ThenBy(x => x.Sku.Code)
            .ThenBy(x => x.Stock.Group.ExpirationDate is null).ThenBy(x => x.Stock.Group.ExpirationDate)
            .ToArray();
        return new CellStockAvailability(location, groups);
    }

    public Task<bool> CanCorrectSkuAsync(ICommandExecutionContext session, Guid skuId, bool adding,
        CancellationToken ct) => RequireSession(session).Db.StockKeepingUnits
        .AnyAsync(x => x.Id == skuId && (!adding || !x.DeletionMark), ct);

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
        var routeReservations = new List<RouteReservationRow>();
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
            routeReservations.AddRange(await db.PickingRouteReservations.AsNoTracking()
                .Where(x => x.WarehouseId == warehouseId && batch.Contains(x.StockKeepingUnitId))
                .Select(x => new RouteReservationRow(new StockGroupKey(x.WarehouseId,
                    x.StorageLocationId, x.StockKeepingUnitId, x.ExpirationDate),
                    new StockRouteReservationView(x.PickingAssemblyId, x.Quantity)))
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
            .Concat(routeReservations.Select(x => x.Group.StorageLocationId))
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
        return BuildAvailability(balances, reservations, routeReservations, shortages, locations, holds);
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
        await session.Db.PickingRouteReservations.Where(x => x.StorageLocationId == hold.StorageLocationId)
            .LoadAsync(ct);
        var pickingOwnerId = hold.OwnerType == StorageLocationLockOwnerType.PickingAssembly
            ? hold.OwnerId : null;
        var reservedByOtherWork = session.Db.PickingReservations.Local.Any(
            x => x.StorageLocationId == hold.StorageLocationId && x.CompletedAtUtc is null
                && x.PickingAssemblyId != pickingOwnerId)
            || session.Db.PickingRouteReservations.Local.Any(
                x => x.StorageLocationId == hold.StorageLocationId && x.PickingAssemblyId != pickingOwnerId);
        if (reservedByOtherWork)
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
            || hold.OwnerId == shortage.PickingAssemblyId
                && hold.OwnerType == StorageLocationLockOwnerType.PickingAssembly;
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
            await db.PickingRouteReservations.Where(x => x.WarehouseId == warehouseId
                && batch.Contains(x.StockKeepingUnitId)
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
        var routeReservations = db.PickingRouteReservations.Local.Where(x => x.WarehouseId == warehouseId
            && skuIds.Contains(x.StockKeepingUnitId)
            && (locationId is null || x.StorageLocationId == locationId)).ToArray();
        var shortages = db.StockShortages.Local.Where(x => x.WarehouseId == warehouseId
            && skuIds.Contains(x.StockKeepingUnitId) && x.ResolvedAtUtc is null
            && (locationId is null || x.StorageLocationId == locationId)).ToArray();
        var locationIds = balances.Select(x => x.StorageLocationId)
            .Concat(reservations.Select(x => x.StorageLocationId))
            .Concat(routeReservations.Select(x => x.StorageLocationId))
            .Concat(shortages.Select(x => x.StorageLocationId)).Distinct().ToArray();
        var locations = new Dictionary<Guid, StorageLocation>();
        foreach (var batch in locationIds.Chunk(500))
        {
            foreach (var location in await db.StorageLocations.Where(x => batch.Contains(x.Id))
                .ToListAsync(ct))
                locations.Add(location.Id, location);
            await db.StorageLocationLocks.Where(x => batch.Contains(x.StorageLocationId)).LoadAsync(ct);
        }

        return BuildAvailability(balances, reservations, routeReservations, shortages, locations,
            db.StorageLocationLocks.Local);
    }

    private static IReadOnlyList<StockAvailability> BuildAvailability(
        IEnumerable<InventoryBalance> balances, IEnumerable<PickingReservation> reservations,
        IEnumerable<PickingRouteReservation> routeReservations,
        IEnumerable<StockShortage> shortages, IReadOnlyDictionary<Guid, StorageLocation> locations,
        IEnumerable<StorageLocationLock> holds) =>
        BuildAvailability(
            balances.Select(x => new BalanceRow(new StockGroupKey(x.WarehouseId,
                x.StorageLocationId, x.StockKeepingUnitId, x.ExpirationDate), x.Quantity)),
            reservations.Select(x => new ReservationRow(new StockGroupKey(x.WarehouseId,
                x.StorageLocationId, x.StockKeepingUnitId, x.ExpirationDate),
                new StockReservationView(x.PickingEntryId, x.PickingAssemblyId, x.Quantity, x.CreatedBy))),
            routeReservations.Select(x => new RouteReservationRow(new StockGroupKey(x.WarehouseId,
                x.StorageLocationId, x.StockKeepingUnitId, x.ExpirationDate),
                new StockRouteReservationView(x.PickingAssemblyId, x.Quantity))),
            shortages.Select(x => new ShortageRow(new StockGroupKey(x.WarehouseId,
                x.StorageLocationId, x.StockKeepingUnitId, x.ExpirationDate), x.BlockedQuantity)),
            locations.ToDictionary(x => x.Key,
                x => new LocationRow(x.Value.Id, x.Value.Code, x.Value.Name)),
            holds.Select(x => new HoldRow(x.StorageLocationId,
                new StockHoldView(x.OwnerType, x.OwnerId, x.Reason, x.LockedBy))));

    private static IReadOnlyList<StockAvailability> BuildAvailability(
        IEnumerable<BalanceRow> balances, IEnumerable<ReservationRow> reservations,
        IEnumerable<RouteReservationRow> routeReservations,
        IEnumerable<ShortageRow> shortages, IReadOnlyDictionary<Guid, LocationRow> locations,
        IEnumerable<HoldRow> holds)
    {
        var balancesByGroup = balances.ToDictionary(x => x.Group, x => x.Quantity);
        var reservationsByGroup = reservations.ToLookup(x => x.Group);
        var routeReservationsByGroup = routeReservations.ToLookup(x => x.Group);
        var shortagesByGroup = shortages.ToLookup(x => x.Group);
        var holdsByLocation = holds.ToDictionary(x => x.StorageLocationId, x => x.Hold);
        var groups = balancesByGroup.Keys.Concat(reservationsByGroup.Select(x => x.Key))
            .Concat(routeReservationsByGroup.Select(x => x.Key))
            .Concat(shortagesByGroup.Select(x => x.Key)).Distinct();
        return groups.Select(key =>
        {
            var accountingQuantity = balancesByGroup.GetValueOrDefault(key);
            var held = holdsByLocation.GetValueOrDefault(key.StorageLocationId);
            var reserved = reservationsByGroup[key].Select(x => x.Reservation).ToArray();
            var blocked = shortagesByGroup[key].Sum(x => x.BlockedQuantity);
            return new StockAvailability(key, locations[key.StorageLocationId].Code,
                accountingQuantity, reserved, blocked, held)
            {
                LocationName = locations[key.StorageLocationId].Name,
                RouteReservations = routeReservationsByGroup[key].Select(x => x.Reservation).ToArray()
            };
        }).OrderBy(x => x.LocationCode, StringComparer.Ordinal)
            .ThenBy(x => x.Group.ExpirationDate is null).ThenBy(x => x.Group.ExpirationDate).ToArray();
    }

    private readonly record struct BalanceRow(StockGroupKey Group, decimal Quantity);
    private readonly record struct ReservationRow(StockGroupKey Group, StockReservationView Reservation);
    private readonly record struct RouteReservationRow(StockGroupKey Group, StockRouteReservationView Reservation);
    private readonly record struct ShortageRow(StockGroupKey Group, decimal BlockedQuantity);
    private readonly record struct LocationRow(Guid Id, string Code, string? Name);
    private readonly record struct HoldRow(Guid StorageLocationId, StockHoldView Hold);
}

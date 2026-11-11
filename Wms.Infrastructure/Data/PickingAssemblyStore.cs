using System.Globalization;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Wms.Application.CommandExecution;
using Wms.Application.Picking;
using Wms.Common;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Data;

public sealed class PickingAssemblyStore(IDbContextFactory<ApplicationDbContext> contextFactory)
    : IPickingAssemblyStore
{
    public async Task<PickingAssembly?> ReadAssemblyAsync(Guid assemblyId, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        return await db.PickingAssemblies.AsNoTracking()
            .Include(x => x.SourceLinks).Include(x => x.PlanLines)
            .SingleOrDefaultAsync(x => x.Id == assemblyId, ct);
    }

    public async Task<ShippingOrder?> ReadSourceAsync(Guid sourceId, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        return await db.ShippingOrders.AsNoTracking().Include(x => x.Items)
            .SingleOrDefaultAsync(x => x.Id == sourceId, ct);
    }

    public async Task<PickingAssembly?> LoadAssemblyAsync(
        ICommandExecutionContext context, Guid assemblyId, CancellationToken ct)
    {
        var session = Session(context);
        await session.BeginSerializableAsync(ct);
        return await session.Db.PickingAssemblies
            .Include(x => x.SourceLinks).Include(x => x.PlanLines)
            .SingleOrDefaultAsync(x => x.Id == assemblyId, ct);
    }

    public Task<ShippingOrder?> LoadSourceAsync(
        ICommandExecutionContext context, Guid sourceId, CancellationToken ct) =>
        Session(context).Db.ShippingOrders.SingleOrDefaultAsync(x => x.Id == sourceId, ct)!;

    public async Task<bool> AreKnownSkusAsync(ICommandExecutionContext context,
        IReadOnlyCollection<Guid> skuIds, CancellationToken ct)
    {
        var distinct = skuIds.Distinct().ToArray();
        if (distinct.Length == 0) return true;
        var count = await Session(context).Db.StockKeepingUnits.CountAsync(
            x => distinct.Contains(x.Id) && !x.DeletionMark, ct);
        return count == distinct.Length;
    }

    public async Task<PickingWorkState> ReadWorkAsync(
        Guid assemblyId, string? locationCode, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        return await ReadWorkCoreAsync(db, assemblyId, locationCode, false, ct);
    }

    public async Task<PickingWorkState> LoadWorkAsync(
        ICommandExecutionContext context, Guid assemblyId, string? locationCode,
        CancellationToken ct)
    {
        var session = Session(context);
        await session.BeginSerializableAsync(ct);
        return await ReadWorkCoreAsync(session.Db, assemblyId, locationCode, true, ct);
    }

    public async Task<PickingCancellationState> LoadCancellationAsync(
        ICommandExecutionContext context, Guid assemblyId, CancellationToken ct)
    {
        var session = Session(context);
        await session.BeginSerializableAsync(ct);
        var db = session.Db;
        var work = await ReadWorkCoreAsync(db, assemblyId, null, true, ct);
        if (work.Assembly is null)
            return new PickingCancellationState(null, [], [], [], []);
        var reservations = await db.PickingReservations
            .Where(x => x.PickingAssemblyId == assemblyId && x.CompletedAtUtc == null)
            .ToArrayAsync(ct);
        var holds = await db.StorageLocationLocks
            .Where(x => x.OwnerType == StorageLocationLockOwnerType.PickingAssembly
                && x.OwnerId == assemblyId).ToArrayAsync(ct);
        var replenishments = await db.ReplenishmentTasks
            .Include(x => x.SourceCycles)
            .Where(x => x.PickingAssemblyId == assemblyId).ToArrayAsync(ct);
        return new PickingCancellationState(work.Assembly, work.Entries,
            reservations, holds, replenishments);
    }

    public async Task<OperationResult> ReleasePickingHoldAsync(
        ICommandExecutionContext context, Guid warehouseId, Guid locationId,
        Guid assemblyId, CancellationToken ct)
    {
        var db = Session(context).Db;
        var locked = await InventoryLocationLock.AcquireAsync(db, [locationId], ct);
        if (!locked.TryGetValue(locationId, out var location)
            || location.WarehouseId != warehouseId)
            return OperationError.NotFound("Ячейка удержания не найдена на складе.");
        var hold = await db.StorageLocationLocks.SingleOrDefaultAsync(
            x => x.StorageLocationId == locationId, ct);
        if (hold is null || hold.OwnerType != StorageLocationLockOwnerType.PickingAssembly
            || hold.OwnerId != assemblyId)
            return OperationError.Conflict("Удержание поддона не принадлежит этой сборке.");
        if (await db.PickingEntries.AnyAsync(x => x.PickingAssemblyId == assemblyId
            && x.StorageLocationId == locationId && x.Quantity > 0, ct))
            return OperationError.Conflict("Перед возвратом поддона верните подтверждённый Отбор из этой ячейки.");
        db.StorageLocationLocks.Remove(hold);
        location.AdvanceOperationalRevision();
        return OperationResult.Success();
    }

    public async Task<OperationResult> ValidatePickingClosureStockAsync(
        ICommandExecutionContext context, PickingAssembly assembly,
        IReadOnlyCollection<PickingEntry> entries, CancellationToken ct)
    {
        var db = Session(context).Db;
        var active = await db.PickingReservations.AsNoTracking()
            .Where(x => x.PickingAssemblyId == assembly.Id && x.CompletedAtUtc == null)
            .ToArrayAsync(ct);
        var picked = entries.Where(x => x.Quantity > 0).ToArray();
        if (active.Length != picked.Length || picked.Any(entry =>
            !active.Any(reservation => reservation.PickingEntryId == entry.Id
                && reservation.WarehouseId == assembly.WarehouseId
                && reservation.StorageLocationId == entry.StorageLocationId
                && reservation.StockKeepingUnitId == entry.StockKeepingUnitId
                && reservation.ExpirationDate == entry.ExpirationDate
                && reservation.Quantity == entry.Quantity)))
            return OperationError.Conflict("Факт Отбора и его резервы не совпадают.");

        var holds = await db.StorageLocationLocks.AsNoTracking()
            .Where(x => x.OwnerType == Wms.Domain.Enums.StorageLocationLockOwnerType.PickingAssembly
                && x.OwnerId == assembly.Id).ToArrayAsync(ct);
        foreach (var hold in holds)
        {
            var balances = await db.InventoryBalances.AsNoTracking()
                .Where(x => x.WarehouseId == assembly.WarehouseId
                    && x.StorageLocationId == hold.StorageLocationId && x.Quantity > 0)
                .ToArrayAsync(ct);
            var cellPicked = picked.Where(x => x.StorageLocationId == hold.StorageLocationId)
                .GroupBy(x => new { x.StockKeepingUnitId, x.ExpirationDate })
                .ToDictionary(x => (x.Key.StockKeepingUnitId, x.Key.ExpirationDate),
                    x => x.Sum(row => row.Quantity));
            if (balances.Length == 0 || balances.Any(balance =>
                    cellPicked.GetValueOrDefault((balance.StockKeepingUnitId,
                        balance.ExpirationDate)) != balance.Quantity)
                || cellPicked.Count != balances.Length)
                return OperationError.Conflict(
                    "Снятый поддон ещё не отобран полностью или не возвращён в исходную ячейку.");
        }
        return OperationResult.Success();
    }

    public async Task ReleasePickingClosureHoldsAsync(
        ICommandExecutionContext context, Guid assemblyId, CancellationToken ct)
    {
        var db = Session(context).Db;
        var holds = await db.StorageLocationLocks
            .Where(x => x.OwnerType == Wms.Domain.Enums.StorageLocationLockOwnerType.PickingAssembly
                && x.OwnerId == assemblyId).ToArrayAsync(ct);
        foreach (var hold in holds.OrderBy(x => x.StorageLocationId))
        {
            var locations = await InventoryLocationLock.AcquireAsync(db,
                [hold.StorageLocationId], ct);
            locations[hold.StorageLocationId].AdvanceOperationalRevision();
            db.StorageLocationLocks.Remove(hold);
        }
    }

    private static async Task<PickingWorkState> ReadWorkCoreAsync(
        ApplicationDbContext db, Guid assemblyId, string? locationCode,
        bool forCommand, CancellationToken ct)
    {
        var assemblies = db.PickingAssemblies.Include(x => x.PlanLines)
            .Include(x => x.SourceLinks);
        var assembly = forCommand
            ? await assemblies.SingleOrDefaultAsync(x => x.Id == assemblyId, ct)
            : await assemblies.AsNoTracking().SingleOrDefaultAsync(x => x.Id == assemblyId, ct);
        if (assembly is null)
            return new PickingWorkState(null, [], null, []);
        var entryQuery = db.PickingEntries.Where(x => x.PickingAssemblyId == assemblyId);
        var entries = forCommand
            ? await entryQuery.ToArrayAsync(ct)
            : await entryQuery.AsNoTracking().ToArrayAsync(ct);
        if (locationCode is null)
            return new PickingWorkState(assembly, entries, null, []);
        var locationId = await db.StorageLocations.AsNoTracking()
            .Where(x => x.WarehouseId == assembly.WarehouseId && x.Code == locationCode)
            .Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct);
        if (locationId is null)
            return new PickingWorkState(assembly, entries, null, []);
        StorageLocation? location;
        if (forCommand)
        {
            var locked = await InventoryLocationLock.AcquireAsync(db, [locationId.Value], ct);
            location = locked.GetValueOrDefault(locationId.Value);
        }
        else
            location = await db.StorageLocations.AsNoTracking()
                .Include(x => x.Zone).Include(x => x.Warehouse).Include(x => x.ActiveLock)
                .SingleOrDefaultAsync(x => x.Id == locationId.Value, ct);
        var skus = await db.InventoryBalances.AsNoTracking()
            .Where(x => x.StorageLocationId == locationId.Value && x.Quantity > 0)
            .Select(x => new PickingCellSku(x.StockKeepingUnitId,
                x.StockKeepingUnit!.Code, x.StockKeepingUnit.Name,
                x.StockKeepingUnit.TracksExpirationDate))
            .Distinct().ToArrayAsync(ct);
        return new PickingWorkState(assembly, entries, location, skus);
    }
    public async Task<Guid?> FindLinkedAssemblyIdAsync(Guid sourceId, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        return await db.PickingAssemblySourceLinks.AsNoTracking()
            .Where(x => x.ShippingOrderId == sourceId && x.DetachedAtUtc == null)
            .Select(x => (Guid?)x.PickingAssemblyId)
            .SingleOrDefaultAsync(ct);
    }

    public async Task<PickingAssemblyChangeState> LoadAsync(
        ICommandExecutionContext context, Guid? assemblyId, Guid sourceId,
        CancellationToken ct)
    {
        var session = Session(context);
        await session.BeginSerializableAsync(ct);
        var db = session.Db;
        var source = await db.ShippingOrders.Include(x => x.Items)
            .SingleOrDefaultAsync(x => x.Id == sourceId, ct);
        var linkedId = await db.PickingAssemblySourceLinks.AsNoTracking()
            .Where(x => x.ShippingOrderId == sourceId && x.DetachedAtUtc == null)
            .Select(x => (Guid?)x.PickingAssemblyId)
            .SingleOrDefaultAsync(ct);
        var selectedId = assemblyId ?? linkedId;
        var assembly = selectedId is Guid id
            ? await db.PickingAssemblies.Include(x => x.SourceLinks)
                .Include(x => x.PlanLines).SingleOrDefaultAsync(x => x.Id == id, ct)
            : null;
        return new PickingAssemblyChangeState(source, assembly, linkedId);
    }

    public async Task<OperationResult> AssignNumberAsync(
        ICommandExecutionContext context, PickingAssembly assembly, CancellationToken ct)
    {
        var db = Session(context).Db;
        var transaction = db.Database.CurrentTransaction
            ?? throw new InvalidOperationException("Номер сборки выделяется внутри транзакции команды.");
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = """
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock
                @Resource = N'picking-assembly-number',
                @LockMode = N'Exclusive',
                @LockOwner = N'Transaction',
                @LockTimeout = 10000;
            SELECT @result;
            """;
        var lockResult = Convert.ToInt32(await command.ExecuteScalarAsync(ct),
            CultureInfo.InvariantCulture);
        if (lockResult < 0)
            return OperationError.Conflict("Не удалось выделить номер сборки; повторите создание.");

        var candidate = assembly.CreatedAtUtc.LocalDateTime;
        while (true)
        {
            var number = candidate.ToString("yyMMdd-HHmmss", CultureInfo.InvariantCulture);
            if (!await db.PickingAssemblies.AnyAsync(x => x.Number == number, ct))
                return assembly.AssignNumber(number);
            try
            {
                candidate = candidate.AddSeconds(1);
            }
            catch (ArgumentOutOfRangeException)
            {
                return OperationError.Conflict("Свободный номер сборки не найден.");
            }
        }
    }

    private static CommandExecutionStore.Session Session(ICommandExecutionContext context) =>
        context as CommandExecutionStore.Session
        ?? throw new InvalidOperationException("Для сборки нужен сеанс складской команды.");
}

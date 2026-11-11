using Microsoft.EntityFrameworkCore;
using Wms.Application.CommandExecution;
using Wms.Application.Replenishment;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Data;

public sealed class ReplenishmentStore(IDbContextFactory<ApplicationDbContext> factory)
    : IReplenishmentStore
{
    public async Task<ReplenishmentTransferState> LoadForTransferAsync(
        ICommandExecutionContext context, Guid taskId,
        string destinationLocationCode, CancellationToken ct)
    {
        var session = context as CommandExecutionStore.Session
            ?? throw new InvalidOperationException("Подпитка требует складскую сессию команды.");
        await session.BeginSerializableAsync(ct);
        var db = session.Db;
        var task = await db.ReplenishmentTasks
            .Include(x => x.SourceCycles).ThenInclude(x => x.Transfers)
            .SingleOrDefaultAsync(x => x.Id == taskId, ct);
        if (task is null)
            return new ReplenishmentTransferState(null, null, null, new HashSet<Guid>());
        var activeCycle = task.SourceCycles.SingleOrDefault(x => x.CompletedAtUtc is null);
        var source = activeCycle is null ? null : await db.StorageLocations
            .Include(x => x.Zone).Include(x => x.Warehouse)
            .Include(x => x.ActiveLock)
            .SingleOrDefaultAsync(x => x.Id == activeCycle.SourceStorageLocationId, ct);
        var destination = await db.StorageLocations
            .Include(x => x.Zone).Include(x => x.Warehouse)
            .Include(x => x.ActiveLock)
            .SingleOrDefaultAsync(x => x.WarehouseId == task.WarehouseId
                && x.Code == destinationLocationCode, ct);
        var skuIds = destination is null ? new HashSet<Guid>()
            : (await db.InventoryBalances.AsNoTracking()
                .Where(x => x.WarehouseId == task.WarehouseId
                    && x.StorageLocationId == destination.Id && x.Quantity != 0)
                .Select(x => x.StockKeepingUnitId).Distinct().ToArrayAsync(ct))
                .ToHashSet();
        return new ReplenishmentTransferState(task, source, destination, skuIds);
    }

    public async Task<ReplenishmentSourceStartState> LoadForSourceStartAsync(
        ICommandExecutionContext context, Guid taskId, string locationCode,
        CancellationToken ct)
    {
        var session = context as CommandExecutionStore.Session
            ?? throw new InvalidOperationException("Подпитка требует складскую сессию команды.");
        await session.BeginSerializableAsync(ct);
        var task = await session.Db.ReplenishmentTasks
            .Include(x => x.SourceCycles)
            .SingleOrDefaultAsync(x => x.Id == taskId, ct);
        var location = task is null ? null
            : await session.Db.StorageLocations
                .Include(x => x.Zone).Include(x => x.Warehouse)
                .Include(x => x.ActiveLock)
                .SingleOrDefaultAsync(x => x.WarehouseId == task.WarehouseId
                    && x.Code == locationCode, ct);
        var assembly = task is null ? null
            : await session.Db.PickingAssemblies.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == task.PickingAssemblyId, ct);
        return new ReplenishmentSourceStartState(task, location, assembly);
    }

    public async Task<IReadOnlyList<ReplenishmentTask>> LoadOrderedTasksAsync(
        ICommandExecutionContext context, Guid warehouseId, Guid skuId,
        CancellationToken ct)
    {
        var db = (context as CommandExecutionStore.Session)?.Db
            ?? throw new InvalidOperationException("Подпитка требует складскую сессию команды.");
        return await db.ReplenishmentTasks.Where(x => x.WarehouseId == warehouseId
                && x.StockKeepingUnitId == skuId
                && x.Status == ReplenishmentTaskStatus.Ordered)
            .OrderBy(x => x.OrderedAtUtc).ThenBy(x => x.Id).ToArrayAsync(ct);
    }

    public async Task<IReadOnlySet<Guid>> ReadReservedLocationIdsAsync(
        ICommandExecutionContext context, Guid warehouseId,
        IReadOnlyCollection<Guid> locationIds, CancellationToken ct)
    {
        var db = (context as CommandExecutionStore.Session)?.Db
            ?? throw new InvalidOperationException("Подпитка требует складскую сессию команды.");
        if (locationIds.Count == 0) return new HashSet<Guid>();
        var ids = locationIds.ToArray();
        return (await db.PickingReservations.AsNoTracking()
            .Where(x => x.WarehouseId == warehouseId
                && ids.Contains(x.StorageLocationId)
                && x.CompletedAtUtc == null)
            .Select(x => x.StorageLocationId).Distinct().ToArrayAsync(ct)).ToHashSet();
    }

    public async Task<IReadOnlyList<ReplenishmentQueueEntry>> ReadQueueAsync(
        Guid warehouseId, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var tasks = await db.ReplenishmentTasks.AsNoTracking()
            .Where(x => x.WarehouseId == warehouseId
                && (x.Status == ReplenishmentTaskStatus.Ordered
                    || x.Status == ReplenishmentTaskStatus.InProgress))
            .ToArrayAsync(ct);
        if (tasks.Length == 0) return [];

        var assemblyIds = tasks.Select(x => x.PickingAssemblyId).Distinct().ToArray();
        var skuIds = tasks.Select(x => x.StockKeepingUnitId).Distinct().ToArray();
        var locationIds = tasks.SelectMany(x => new[]
            { x.RecommendedSourceStorageLocationId,
                x.RecommendedDestinationStorageLocationId })
            .Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToArray();
        var numbers = await db.PickingAssemblies.AsNoTracking()
            .Where(x => assemblyIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Number, ct);
        var skus = await db.StockKeepingUnits.AsNoTracking()
            .Where(x => skuIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Code, x.Name })
            .ToDictionaryAsync(x => x.Id, ct);
        var codes = await db.StorageLocations.AsNoTracking()
            .Where(x => locationIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Code, ct);
        var currentQueues = await (
            from link in db.PickingAssemblySourceLinks.AsNoTracking()
            join source in db.ShippingOrders.AsNoTracking()
                on link.ShippingOrderId equals source.Id
            where link.DetachedAtUtc == null
                && assemblyIds.Contains(link.PickingAssemblyId)
            select new { link.PickingAssemblyId, source.Queue })
            .ToArrayAsync(ct);
        var queues = currentQueues.ToLookup(x => x.PickingAssemblyId,
            x => x.Queue);

        return tasks.Select(task =>
        {
            skus.TryGetValue(task.StockKeepingUnitId, out var sku);
            return new ReplenishmentQueueEntry(task.Id, task.PickingAssemblyId,
                numbers.GetValueOrDefault(task.PickingAssemblyId) ?? "",
                task.StockKeepingUnitId, sku?.Code, sku?.Name,
                task.OrderedQuantity, task.TransferredQuantity, task.Status,
                task.AssignedTo, task.OrderedAtUtc,
                task.RecommendedSourceStorageLocationId,
                task.RecommendedSourceStorageLocationId is Guid sourceId
                    ? codes.GetValueOrDefault(sourceId) : null,
                task.RecommendedDestinationStorageLocationId,
                task.RecommendedDestinationStorageLocationId is Guid destinationId
                    ? codes.GetValueOrDefault(destinationId) : null,
                queues[task.PickingAssemblyId].ToArray());
        }).ToArray();
    }

    public async Task<ReplenishmentProposalState> LoadProposalStateAsync(
        ICommandExecutionContext context, Guid warehouseId,
        IReadOnlyCollection<Guid> skuIds, CancellationToken ct)
    {
        var db = (context as CommandExecutionStore.Session)?.Db
            ?? throw new InvalidOperationException("Подпитка требует складскую сессию команды.");
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Предложения Подпитки читаются в транзакции команды.");

        var locations = await db.StorageLocations.AsNoTracking()
            .Include(x => x.Zone).Include(x => x.Warehouse)
            .Include(x => x.ActiveLock)
            .Where(x => x.WarehouseId == warehouseId && !x.IsFolder)
            .ToArrayAsync(ct);
        var ids = locations.Select(x => x.Id).ToArray();
        var current = await db.InventoryBalances.AsNoTracking()
            .Where(x => x.WarehouseId == warehouseId && x.Quantity > 0
                && ids.Contains(x.StorageLocationId))
            .Select(x => new { x.StorageLocationId, x.StockKeepingUnitId })
            .Distinct().ToArrayAsync(ct);
        var relevantSkus = skuIds.Distinct().ToArray();
        var history = await db.InventoryTurnovers.AsNoTracking()
            .Where(x => x.WarehouseId == warehouseId
                && relevantSkus.Contains(x.StockKeepingUnitId)
                && ids.Contains(x.StorageLocationId))
            .Select(x => new { x.StorageLocationId, x.StockKeepingUnitId })
            .Distinct().ToArrayAsync(ct);
        var currentByLocation = current.ToLookup(x => x.StorageLocationId,
            x => x.StockKeepingUnitId);
        var historyByLocation = history.ToLookup(x => x.StorageLocationId,
            x => x.StockKeepingUnitId);
        var views = locations.Select(x => new ReplenishmentLocation(x.Id, x.Code,
            x.Zone?.Type ?? ZoneType.Receiving,
            !x.DeletionMark && x.Zone is { DeletionMark: false }
                && x.Warehouse is { DeletionMark: false },
            x.ActiveLock is not null, x.AllowsMultipleSkus,
            currentByLocation[x.Id].ToHashSet(),
            historyByLocation[x.Id].ToHashSet())).ToArray();
        var tasks = await db.ReplenishmentTasks.AsNoTracking()
            .Where(x => x.WarehouseId == warehouseId
                && (x.Status == ReplenishmentTaskStatus.Ordered
                    || x.Status == ReplenishmentTaskStatus.InProgress))
            .ToArrayAsync(ct);
        return new ReplenishmentProposalState(views, tasks);
    }
}

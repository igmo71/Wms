using Microsoft.EntityFrameworkCore;
using Wms.Application.CommandExecution;
using Wms.Application.Inventory;
using Wms.Application.Replenishment;
using Wms.Common;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Data;

public sealed class ReplenishmentStore(IDbContextFactory<ApplicationDbContext> factory)
    : IReplenishmentStore
{
    public async Task<ReplenishmentActiveSourceState> LoadActiveSourceAsync(
        ICommandExecutionContext context, Guid taskId, CancellationToken ct)
    {
        var session = context as CommandExecutionStore.Session
            ?? throw new InvalidOperationException("Подпитка требует складскую сессию команды.");
        await session.BeginSerializableAsync(ct);
        var task = await session.Db.ReplenishmentTasks
            .Include(x => x.SourceCycles).ThenInclude(x => x.Transfers)
            .SingleOrDefaultAsync(x => x.Id == taskId, ct);
        var cycle = task?.SourceCycles.SingleOrDefault(x => x.CompletedAtUtc is null);
        var source = cycle is null ? null : await session.Db.StorageLocations
            .Include(x => x.Zone).Include(x => x.Warehouse)
            .Include(x => x.ActiveLock)
            .SingleOrDefaultAsync(x => x.Id == cycle.SourceStorageLocationId, ct);
        return new ReplenishmentActiveSourceState(task, source);
    }

    public async Task<bool> HasUnblockedStockAsync(ICommandExecutionContext context,
        Guid warehouseId, Guid locationId, CancellationToken ct)
    {
        var db = (context as CommandExecutionStore.Session)?.Db
            ?? throw new InvalidOperationException("Подпитка требует складскую сессию команды.");
        var balances = await db.InventoryBalances.AsNoTracking()
            .Where(x => x.WarehouseId == warehouseId && x.StorageLocationId == locationId
                && x.Quantity > 0)
            .Select(x => new { x.StockKeepingUnitId, x.ExpirationDate, x.Quantity })
            .ToArrayAsync(ct);
        var shortages = await db.StockShortages.AsNoTracking()
            .Where(x => x.WarehouseId == warehouseId && x.StorageLocationId == locationId
                && x.ResolvedAtUtc == null)
            .ToArrayAsync(ct);
        return balances.Any(balance => balance.Quantity > shortages
            .Where(x => x.StockKeepingUnitId == balance.StockKeepingUnitId
                && x.ExpirationDate == balance.ExpirationDate)
            .Sum(x => x.BlockedQuantity));
    }

    public async Task<OperationResult> ReleaseSourceHoldAsync(
        ICommandExecutionContext context, Guid warehouseId, Guid locationId,
        Guid taskId, CancellationToken ct)
    {
        var db = (context as CommandExecutionStore.Session)?.Db
            ?? throw new InvalidOperationException("Подпитка требует складскую сессию команды.");
        var locations = await InventoryLocationLock.AcquireAsync(db, [locationId], ct);
        if (!locations.TryGetValue(locationId, out var location)
            || location.WarehouseId != warehouseId)
            return OperationError.NotFound("Исходная ячейка Подпитки не найдена на складе.");
        var hold = await db.StorageLocationLocks.SingleOrDefaultAsync(
            x => x.StorageLocationId == locationId, ct);
        if (hold?.OwnerType != StorageLocationLockOwnerType.ReplenishmentTask
            || hold.OwnerId != taskId)
            return OperationError.Conflict("Исходный поддон больше не удерживается этим заданием.");
        db.StorageLocationLocks.Remove(hold);
        location.AdvanceOperationalRevision();
        return OperationResult.Success();
    }

    public async Task<IReadOnlyList<ReplenishmentTask>> LoadForAssemblyAsync(
        ICommandExecutionContext context, Guid assemblyId, CancellationToken ct)
    {
        var session = context as CommandExecutionStore.Session
            ?? throw new InvalidOperationException("Подпитка требует складскую сессию команды.");
        if (session.Db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Задания сборки читаются в транзакции команды.");
        return await session.Db.ReplenishmentTasks
            .Include(x => x.SourceCycles)
            .Where(x => x.PickingAssemblyId == assemblyId).ToArrayAsync(ct);
    }

    public async Task<PickingAssembly?> LoadCancellationAssemblyAsync(
        ICommandExecutionContext context, Guid assemblyId, CancellationToken ct)
    {
        var session = context as CommandExecutionStore.Session
            ?? throw new InvalidOperationException("Подпитка требует складскую сессию команды.");
        if (session.Db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Отмена сборки читается в транзакции команды.");
        return await session.Db.PickingAssemblies.Include(x => x.SourceLinks)
            .SingleOrDefaultAsync(x => x.Id == assemblyId, ct);
    }

    public async Task<ReplenishmentTransferCorrectionState?> LoadTransferCorrectionAsync(
        ICommandExecutionContext context, Guid transferId, CancellationToken ct)
    {
        var session = context as CommandExecutionStore.Session
            ?? throw new InvalidOperationException("Подпитка требует складскую сессию команды.");
        await session.BeginSerializableAsync(ct);
        var task = await session.Db.ReplenishmentTasks
            .Include(x => x.SourceCycles).ThenInclude(x => x.Transfers)
            .SingleOrDefaultAsync(x => x.SourceCycles.Any(c =>
                c.Transfers.Any(t => t.Id == transferId)), ct);
        if (task is null) return null;
        var cycle = task.SourceCycles.Single(x => x.Transfers.Any(t => t.Id == transferId));
        var transfer = cycle.Transfers.Single(x => x.Id == transferId);
        var corrections = await session.Db.InventoryMovements.AsNoTracking()
            .Where(x => x.RecorderType == RecorderType.StockCorrection
                && x.RecorderId == transferId && x.PostedAtUtc != null)
            .ToArrayAsync(ct);
        return new ReplenishmentTransferCorrectionState(task, cycle, transfer,
            EffectiveQuantity(task, transfer, cycle, corrections), corrections.Length);
    }

    public async Task<IReadOnlyList<ReplenishmentTransferView>> ReadTransfersAsync(
        Guid assemblyId, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var tasks = await db.ReplenishmentTasks.AsNoTracking()
            .Include(x => x.SourceCycles).ThenInclude(x => x.Transfers)
            .Where(x => x.PickingAssemblyId == assemblyId).ToArrayAsync(ct);
        var steps = tasks.SelectMany(task => task.SourceCycles.SelectMany(cycle =>
            cycle.Transfers.Select(transfer => new { task, cycle, transfer })))
            .ToArray();
        if (steps.Length == 0) return [];
        var transferIds = steps.Select(x => x.transfer.Id).ToArray();
        var locationIds = steps.SelectMany(x => new[]
            { x.cycle.SourceStorageLocationId, x.transfer.DestinationStorageLocationId })
            .Distinct().ToArray();
        var skuIds = tasks.Select(x => x.StockKeepingUnitId).Distinct().ToArray();
        var corrections = await db.InventoryMovements.AsNoTracking()
            .Where(x => x.RecorderType == RecorderType.StockCorrection
                && x.RecorderId != null && transferIds.Contains(x.RecorderId.Value)
                && x.PostedAtUtc != null)
            .ToArrayAsync(ct);
        var locations = await db.StorageLocations.AsNoTracking()
            .Where(x => locationIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Code, ct);
        var skus = await db.StockKeepingUnits.AsNoTracking()
            .Where(x => skuIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name ?? x.Code, ct);
        var byTransfer = corrections.ToLookup(x => x.RecorderId);
        return steps.OrderByDescending(x => x.transfer.ConfirmedAtUtc)
            .ThenBy(x => x.transfer.Id)
            .Select(x => new ReplenishmentTransferView(x.transfer.Id,
                x.task.Id, x.task.StockKeepingUnitId,
                skus.GetValueOrDefault(x.task.StockKeepingUnitId),
                locations.GetValueOrDefault(x.cycle.SourceStorageLocationId)
                    ?? x.cycle.SourceStorageLocationId.ToString(),
                locations.GetValueOrDefault(x.transfer.DestinationStorageLocationId)
                    ?? x.transfer.DestinationStorageLocationId.ToString(),
                x.transfer.ExpirationDate, x.transfer.Quantity,
                EffectiveQuantity(x.task, x.transfer, x.cycle,
                    byTransfer[x.transfer.Id]),
                x.transfer.ConfirmedAtUtc)).ToArray();
    }

    public async Task<ReplenishmentTaskDetail?> ReadTaskAsync(
        Guid taskId, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var task = await db.ReplenishmentTasks.AsNoTracking()
            .Include(x => x.SourceCycles).ThenInclude(x => x.Transfers)
            .SingleOrDefaultAsync(x => x.Id == taskId, ct);
        if (task is null) return null;
        var cycles = task.SourceCycles.OrderBy(x => x.StartedAtUtc).ToArray();
        var transfers = cycles.SelectMany(x => x.Transfers).ToArray();
        var transferIds = transfers.Select(x => x.Id).ToArray();
        var corrections = await db.InventoryMovements.AsNoTracking()
            .Where(x => x.RecorderType == RecorderType.StockCorrection
                && x.RecorderId != null && transferIds.Contains(x.RecorderId.Value)
                && x.PostedAtUtc != null).ToArrayAsync(ct);
        var correctionLookup = corrections.ToLookup(x => x.RecorderId);
        var locationIds = cycles.Select(x => x.SourceStorageLocationId)
            .Concat(transfers.Select(x => x.DestinationStorageLocationId))
            .Concat(new[] { task.RecommendedSourceStorageLocationId,
                task.RecommendedDestinationStorageLocationId }
                .OfType<Guid>()).Distinct().ToArray();
        var addresses = await db.StorageLocations.AsNoTracking()
            .Where(x => locationIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Code, x.Name })
            .ToDictionaryAsync(x => x.Id, ct);
        var codes = addresses.ToDictionary(x => x.Key, x => x.Value.Code);
        var names = addresses.ToDictionary(x => x.Key, x => x.Value.Name);
        var active = cycles.SingleOrDefault(x => x.CompletedAtUtc is null);
        InventoryBalance[] balances = active is null ? [] : await db.InventoryBalances.AsNoTracking()
            .Where(x => x.WarehouseId == task.WarehouseId
                && x.StorageLocationId == active.SourceStorageLocationId
                && x.Quantity > 0).ToArrayAsync(ct);
        StockShortage[] shortages = active is null ? [] : await db.StockShortages.AsNoTracking()
            .Where(x => x.WarehouseId == task.WarehouseId
                && x.StorageLocationId == active.SourceStorageLocationId
                && x.ResolvedAtUtc == null).ToArrayAsync(ct);
        var skuIds = balances.Select(x => x.StockKeepingUnitId)
            .Append(task.StockKeepingUnitId).Distinct().ToArray();
        var skus = await db.StockKeepingUnits.AsNoTracking()
            .Where(x => skuIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name ?? x.Code, ct);
        return new ReplenishmentTaskDetail(task.Id, task.PickingAssemblyId,
            task.WarehouseId, task.StockKeepingUnitId,
            skus.GetValueOrDefault(task.StockKeepingUnitId),
            task.OrderedQuantity, task.TransferredQuantity, task.Status,
            task.AssignedTo, task.OrderedAtUtc,
            task.RecommendedSourceStorageLocationId,
            task.RecommendedSourceStorageLocationId is Guid recommendedSourceId
                ? codes.GetValueOrDefault(recommendedSourceId) : null,
            task.RecommendedDestinationStorageLocationId,
            task.RecommendedDestinationStorageLocationId is Guid recommendedDestinationId
                ? codes.GetValueOrDefault(recommendedDestinationId) : null,
            cycles.Select(cycle => new ReplenishmentTaskCycleView(cycle.Id,
                cycle.SourceStorageLocationId,
                codes.GetValueOrDefault(cycle.SourceStorageLocationId)
                    ?? cycle.SourceStorageLocationId.ToString(),
                cycle.StartedAtUtc, cycle.CompletedAtUtc, cycle.SourceWasEmpty,
                cycle.Transfers.OrderBy(x => x.ConfirmedAtUtc)
                    .Select(transfer => new ReplenishmentTaskTransferView(
                        transfer.Id, transfer.DestinationStorageLocationId,
                        codes.GetValueOrDefault(transfer.DestinationStorageLocationId)
                            ?? transfer.DestinationStorageLocationId.ToString(),
                        transfer.ExpirationDate, transfer.Quantity,
                        EffectiveQuantity(task, transfer, cycle,
                            correctionLookup[transfer.Id]),
                        transfer.ConfirmedAtUtc)
                    { DestinationName = names.GetValueOrDefault(transfer.DestinationStorageLocationId) })
                    .ToArray())
                { SourceName = names.GetValueOrDefault(cycle.SourceStorageLocationId) })
                .ToArray(),
            balances.Select(balance => new ReplenishmentSourceStockView(
                balance.StockKeepingUnitId,
                skus.GetValueOrDefault(balance.StockKeepingUnitId),
                balance.ExpirationDate, balance.Quantity,
                shortages.Where(x => x.StockKeepingUnitId == balance.StockKeepingUnitId
                    && x.ExpirationDate == balance.ExpirationDate)
                    .Sum(x => x.BlockedQuantity)))
                .OrderBy(x => x.SkuId != task.StockKeepingUnitId)
                .ThenBy(x => x.ExpirationDate is null)
                .ThenBy(x => x.ExpirationDate).ToArray())
        {
            RecommendedSourceName = task.RecommendedSourceStorageLocationId is Guid sourceNameId
                ? names.GetValueOrDefault(sourceNameId) : null,
            RecommendedDestinationName = task.RecommendedDestinationStorageLocationId is Guid destinationNameId
                ? names.GetValueOrDefault(destinationNameId) : null
        };
    }

    private static decimal EffectiveQuantity(ReplenishmentTask task,
        ReplenishmentTransfer transfer,
        ReplenishmentSourceCycle cycle, IEnumerable<InventoryMovement> corrections)
    {
        var effective = transfer.Quantity;
        foreach (var movement in corrections)
        {
            if (movement.WarehouseId == task.WarehouseId
                && movement.StockKeepingUnitId == task.StockKeepingUnitId
                && movement.SourceStorageLocationId == cycle.SourceStorageLocationId
                && movement.DestinationStorageLocationId == transfer.DestinationStorageLocationId
                && movement.ExpirationDate == transfer.ExpirationDate)
                effective += movement.Quantity;
            else if (movement.WarehouseId == task.WarehouseId
                && movement.StockKeepingUnitId == task.StockKeepingUnitId
                && movement.SourceStorageLocationId == transfer.DestinationStorageLocationId
                && movement.DestinationStorageLocationId == cycle.SourceStorageLocationId
                && movement.ExpirationDate == transfer.ExpirationDate)
                effective -= movement.Quantity;
            else
                throw new InvalidOperationException("Движение корректировки не соответствует шагу Подпитки.");
        }
        return effective;
    }

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
        var addresses = await db.StorageLocations.AsNoTracking()
            .Where(x => locationIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Code, x.Name })
            .ToDictionaryAsync(x => x.Id, ct);
        var codes = addresses.ToDictionary(x => x.Key, x => x.Value.Code);
        var names = addresses.ToDictionary(x => x.Key, x => x.Value.Name);
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
                queues[task.PickingAssemblyId].ToArray())
            {
                RecommendedSourceName = task.RecommendedSourceStorageLocationId is Guid sourceNameId
                    ? names.GetValueOrDefault(sourceNameId) : null,
                RecommendedDestinationName = task.RecommendedDestinationStorageLocationId is Guid destinationNameId
                    ? names.GetValueOrDefault(destinationNameId) : null
            };
        }).ToArray();
    }

    public async Task<ReplenishmentProposalState> LoadProposalStateAsync(
        ICommandExecutionContext context, Guid warehouseId,
        IReadOnlyList<StockAvailability> availability, CancellationToken ct)
    {
        var db = (context as CommandExecutionStore.Session)?.Db
            ?? throw new InvalidOperationException("Подпитка требует складскую сессию команды.");
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Предложения Подпитки читаются в транзакции команды.");

        // Command route ranking uses the full address set.
        return await ReadProposalStateCoreAsync(db, warehouseId, availability, [],
            includeEmptyLocations: true, ct);
    }

    public async Task<ReplenishmentProposalState> ReadProposalStateAsync(
        Guid warehouseId, IReadOnlyList<StockAvailability> availability,
        IReadOnlyCollection<Guid> taskLocationIds, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await ReadProposalStateCoreAsync(db, warehouseId, availability, taskLocationIds,
            includeEmptyLocations: false, ct);
    }

    public async Task<IReadOnlyList<ReplenishmentTask>> ReadAssemblyTasksAsync(
        Guid assemblyId, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.ReplenishmentTasks.AsNoTracking()
            .Where(x => x.PickingAssemblyId == assemblyId)
            .OrderBy(x => x.OrderedAtUtc).ThenBy(x => x.Id).ToArrayAsync(ct);
    }

    private static async Task<ReplenishmentProposalState> ReadProposalStateCoreAsync(
        ApplicationDbContext db, Guid warehouseId, IReadOnlyList<StockAvailability> availability,
        IReadOnlyCollection<Guid> taskLocationIds, bool includeEmptyLocations, CancellationToken ct)
    {
        var stock = availability.Where(x => x.Group.WarehouseId == warehouseId).ToArray();
        var stockLocationIds = stock.Select(x => x.Group.StorageLocationId).Distinct().ToArray();
        var relevantSkusByLocation = stock.Where(x => x.AccountingQuantity > 0)
            .ToLookup(x => x.Group.StorageLocationId, x => x.Group.StockKeepingUnitId);
        var counts = new Dictionary<Guid, int>();
        foreach (var batch in stockLocationIds.Chunk(500))
        {
            var rows = await db.InventoryBalances.AsNoTracking()
                .Where(x => x.WarehouseId == warehouseId && x.Quantity > 0
                    && batch.Contains(x.StorageLocationId))
                .GroupBy(x => x.StorageLocationId)
                .Select(group => new
                {
                    LocationId = group.Key,
                    Count = group.Select(x => x.StockKeepingUnitId).Distinct().Count()
                }).ToArrayAsync(ct);
            foreach (var row in rows) counts.Add(row.LocationId, row.Count);
        }

        var warehouseLocations = db.StorageLocations.AsNoTracking()
            .Where(x => x.WarehouseId == warehouseId && !x.IsFolder);
        var locationIds = stockLocationIds.Concat(taskLocationIds).Distinct().ToArray();
        IEnumerable<Guid[]> locationBatches = includeEmptyLocations
            ? new[] { Array.Empty<Guid>() } : locationIds.Chunk(500);
        var views = new List<ReplenishmentLocation>();
        foreach (var batch in locationBatches)
        {
            var query = warehouseLocations;
            if (!includeEmptyLocations) query = query.Where(x => batch.Contains(x.Id));
            var locations = await query.Select(x => new
            {
                x.Id, x.Code, x.Name, x.AllowsMultipleSkus,
                ZoneType = x.Zone == null ? ZoneType.Receiving : x.Zone.Type,
                IsActive = !x.DeletionMark && x.Zone != null && !x.Zone.DeletionMark
                    && x.Warehouse != null && !x.Warehouse.DeletionMark,
                IsHeld = x.ActiveLock != null
            }).ToArrayAsync(ct);
            views.AddRange(locations.Select(x => new ReplenishmentLocation(x.Id, x.Code,
                x.ZoneType, x.IsActive, x.IsHeld, x.AllowsMultipleSkus,
                counts.GetValueOrDefault(x.Id),
                x.ZoneType == ZoneType.Storage && x.IsActive && !x.IsHeld
                    ? relevantSkusByLocation[x.Id].ToHashSet() : new HashSet<Guid>())
                { Name = x.Name }));
        }
        var tasks = await db.ReplenishmentTasks.AsNoTracking()
            .Where(x => x.WarehouseId == warehouseId
                && (x.Status == ReplenishmentTaskStatus.Ordered
                    || x.Status == ReplenishmentTaskStatus.InProgress))
            .ToArrayAsync(ct);
        return new ReplenishmentProposalState(views, tasks);
    }
}

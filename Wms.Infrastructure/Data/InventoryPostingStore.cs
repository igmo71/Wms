using Microsoft.EntityFrameworkCore;
using Wms.Application.CommandExecution;
using Wms.Application.Inventory;
using Wms.Application.OpeningImports;
using Wms.Domain;

namespace Wms.Data;

public sealed class InventoryPostingStore : IInventoryPostingStore,
    IOpeningInventoryPostingStore
{
    public async Task<OpeningInventoryPostingState> LoadOpeningAsync(
        ICommandExecutionContext context, Guid warehouseId,
        IReadOnlyCollection<Guid> locationIds, CancellationToken ct)
    {
        if (context is not CommandExecutionStore.Session session)
            throw new InvalidOperationException("Загрузка остатков требует сессию команды.");

        var db = session.Db;
        var locations = new Dictionary<Guid, StorageLocation>();
        foreach (var batch in locationIds.Distinct().Chunk(500))
        {
            var loaded = await db.StorageLocations
                .Include(x => x.Warehouse)
                .Include(x => x.Zone)
                .Include(x => x.ActiveLock)
                .Where(x => x.WarehouseId == warehouseId && batch.Contains(x.Id))
                .ToArrayAsync(ct);
            foreach (var location in loaded)
                locations.Add(location.Id, location);
        }
        var balances = await db.InventoryBalances
            .Where(x => x.WarehouseId == warehouseId)
            .ToArrayAsync(ct);
        return new OpeningInventoryPostingState(locations,
            balances.ToDictionary(x => new OpeningBalanceKey(
                x.WarehouseId, x.StorageLocationId,
                x.StockKeepingUnitId, x.ExpirationDate)));
    }

    public async Task<InventoryPostingState> LoadAsync(
        ICommandExecutionContext context,
        InventoryMovement movement,
        CancellationToken ct)
    {
        if (context is not CommandExecutionStore.Session session)
            throw new InvalidOperationException("Проведение должно использовать сессию команды.");

        var db = session.Db;
        var locationIds = new[]
            { movement.SourceStorageLocationId, movement.DestinationStorageLocationId }
            .OfType<Guid>().Distinct().ToArray();
        var locations = await db.StorageLocations
            .Include(x => x.Warehouse)
            .Include(x => x.Zone)
            .Include(x => x.ActiveLock)
            .Where(x => locationIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);
        var balances = await db.InventoryBalances
            .Where(x => x.WarehouseId == movement.WarehouseId
                && locationIds.Contains(x.StorageLocationId)
                && x.StockKeepingUnitId == movement.StockKeepingUnitId
                && x.ExpirationDate == movement.ExpirationDate)
            .ToDictionaryAsync(x => x.StorageLocationId, ct);
        foreach (var added in db.ChangeTracker.Entries<InventoryBalance>()
            .Where(x => x.State == EntityState.Added)
            .Select(x => x.Entity)
            .Where(x => x.WarehouseId == movement.WarehouseId
                && locationIds.Contains(x.StorageLocationId)
                && x.StockKeepingUnitId == movement.StockKeepingUnitId
                && x.ExpirationDate == movement.ExpirationDate))
            balances.Add(added.StorageLocationId, added);
        return new InventoryPostingState(locations, balances);
    }

    public async Task<bool> HasMultipleNonzeroSkusAsync(
        ICommandExecutionContext context, Guid warehouseId,
        Guid storageLocationId, CancellationToken ct)
    {
        if (context is not CommandExecutionStore.Session session)
            throw new InvalidOperationException("Проведение должно использовать сессию команды.");

        var db = session.Db;
        var saved = await db.InventoryBalances
            .Where(x => x.WarehouseId == warehouseId
                && x.StorageLocationId == storageLocationId)
            .ToListAsync(ct);
        var added = db.ChangeTracker.Entries<InventoryBalance>()
            .Where(x => x.State == EntityState.Added)
            .Select(x => x.Entity)
            .Where(x => x.WarehouseId == warehouseId
                && x.StorageLocationId == storageLocationId);
        return saved.Concat(added)
            .Where(x => x.Quantity != 0)
            .Select(x => x.StockKeepingUnitId)
            .Distinct()
            .Take(2)
            .Count() > 1;
    }
}

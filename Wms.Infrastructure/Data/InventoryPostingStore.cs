using Microsoft.EntityFrameworkCore;
using Wms.Application.CommandExecution;
using Wms.Application.Inventory;
using Wms.Domain;

namespace Wms.Data;

public sealed class InventoryPostingStore : IInventoryPostingStore
{
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

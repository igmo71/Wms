using Microsoft.EntityFrameworkCore;
using Wms.Application.CommandExecution;
using Wms.Application.Inventory.Counts;
using Wms.Common;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Data;

public sealed class InventoryCountStore : IInventoryCountStore
{
    public Task<Guid?> FindLocationIdAsync(ICommandExecutionContext session,
        Guid warehouseId, string code, CancellationToken ct) =>
        Db(session).StorageLocations.AsNoTracking()
            .Where(x => x.WarehouseId == warehouseId && x.Code == code)
            .Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct);

    public async Task<StorageLocation?> LockLocationAsync(ICommandExecutionContext session,
        Guid locationId, CancellationToken ct) =>
        (await InventoryLocationLock.AcquireAsync(Db(session), [locationId], ct)).GetValueOrDefault(locationId);

    public async Task<(InventoryCount Count, StorageLocation Location)?> LoadDraftAsync(
        ICommandExecutionContext session, Guid countId, CancellationToken ct)
    {
        var db = Db(session);
        var locationId = await db.InventoryCounts.AsNoTracking()
            .Where(x => x.Id == countId && x.Status == InventoryCountStatus.Draft)
            .Select(x => (Guid?)x.StorageLocationId).SingleOrDefaultAsync(ct);
        if (locationId is not Guid id)
            return null;
        var location = await LockLocationAsync(session, id, ct);
        if (location is null
            || location.ActiveLock is not { OwnerType: StorageLocationLockOwnerType.InventoryCount } hold
            || hold.OwnerId != countId)
            return null;
        var count = await db.InventoryCounts.Include(x => x.Items)
            .ThenInclude(x => x.StockKeepingUnit)
            .SingleOrDefaultAsync(x => x.Id == countId && x.Status == InventoryCountStatus.Draft, ct);
        return count is null ? null : (count, location);
    }

    public async Task<IReadOnlyList<InventoryCountBalance>> LoadBalancesAsync(ICommandExecutionContext session,
        Guid warehouseId, Guid locationId, CancellationToken ct) =>
        await Db(session).InventoryBalances.AsNoTracking()
            .Where(x => x.WarehouseId == warehouseId && x.StorageLocationId == locationId && x.Quantity != 0)
            .OrderBy(x => x.StockKeepingUnitId).ThenBy(x => x.ExpirationDate)
            .Select(x => new InventoryCountBalance(x.StockKeepingUnitId, x.ExpirationDate, x.Quantity))
            .ToListAsync(ct);

    public Task<InventoryCountStockCommitments> GetStockCommitmentsAsync(ICommandExecutionContext session,
        Guid locationId, CancellationToken ct)
    {
        var db = Db(session);
        return db.StorageLocations.AsNoTracking().Where(location => location.Id == locationId)
            .Select(location => new InventoryCountStockCommitments(
                db.PickingRouteReservations.Any(x => x.StorageLocationId == location.Id),
                db.PickingReservations.Any(x => x.StorageLocationId == location.Id && x.CompletedAtUtc == null),
                db.StockShortages.Any(x => x.StorageLocationId == location.Id && x.ResolvedAtUtc == null)))
            .SingleAsync(ct);
    }

    public async Task<OperationResult<StockKeepingUnit>> ResolveSkuAsync(ICommandExecutionContext session,
        string? barcode, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(barcode))
            return OperationError.Invalid("Штрихкод товара не указан.");
        var matches = await Db(session).SkuBarcodes.AsNoTracking()
            .Where(x => x.Value == barcode).Select(x => x.Sku!).Take(2).ToListAsync(ct);
        return matches.Count switch
        {
            0 => OperationError.NotFound("Товар с таким штрихкодом не найден."),
            1 => matches[0],
            _ => OperationError.Conflict("Штрихкод соответствует нескольким товарам.")
        };
    }

    public Task<StockKeepingUnit?> LoadSkuAsync(ICommandExecutionContext session, Guid skuId, CancellationToken ct) =>
        Db(session).StockKeepingUnits.AsNoTracking().SingleOrDefaultAsync(x => x.Id == skuId, ct);

    public void Remove(ICommandExecutionContext session, object entity) => Db(session).Remove(entity);

    private static ApplicationDbContext Db(ICommandExecutionContext session) =>
        session is CommandExecutionStore.Session command ? command.Db
            : throw new InvalidOperationException("Инвентаризация требует сессию команды.");
}

using Microsoft.EntityFrameworkCore;
using Wms.Application.CommandExecution;
using Wms.Application.Receiving;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Data;

public sealed class ReceivingLpnDeletionStore(
    IDbContextFactory<ApplicationDbContext> contextFactory) : IReceivingLpnDeletionStore
{
    public async Task<ReceivingLpnDeletionState?> LoadPreviewAsync(
        Guid orderId, Guid lpnId, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        return await LoadAsync(db, orderId, lpnId, ct);
    }

    public Task<ReceivingLpnDeletionState?> LoadForDeletionAsync(
        ICommandExecutionContext session, Guid orderId, Guid lpnId, CancellationToken ct) =>
        LoadAsync(session is CommandExecutionStore.Session commandSession
            ? commandSession.Db
            : throw new InvalidOperationException("Удаление LPN требует сессию команды."),
            orderId, lpnId, ct);

    private static async Task<ReceivingLpnDeletionState?> LoadAsync(
        ApplicationDbContext db, Guid orderId, Guid lpnId, CancellationToken ct)
    {
        var order = await db.ReceivingOrders
            .Include(x => x.Items)
            .Include(x => x.LicensePlateNumbers).ThenInclude(x => x.Items)
            .AsSplitQuery().SingleOrDefaultAsync(x => x.Id == orderId, ct);
        if (order is null)
            return null;
        var lpn = await db.LicensePlateNumbers
            .Include(x => x.Items).Include(x => x.Putaways).AsSplitQuery()
            .SingleOrDefaultAsync(x => x.Id == lpnId && x.ReceivingOrderId == orderId, ct);
        if (lpn is null)
            return null;

        var movements = await db.InventoryMovements
            .Where(x => x.LicensePlateNumberId == lpnId
                && x.RecorderType == RecorderType.ReceivingOrder
                && x.RecorderId == orderId && x.PostedAtUtc != null)
            .ToArrayAsync(ct);
        var originals = movements.Where(x => x.ReversedMovementId is null).ToArray();
        var locationIds = originals.SelectMany(x => new[]
            { x.SourceStorageLocationId, x.DestinationStorageLocationId })
            .OfType<Guid>().Distinct().ToArray();
        var skuIds = originals.Select(x => x.StockKeepingUnitId).Distinct().ToArray();
        var locations = await db.StorageLocations
            .Where(x => locationIds.Contains(x.Id)).ToArrayAsync(ct);
        var candidates = await db.InventoryBalances
            .Where(x => x.WarehouseId == order.WarehouseId
                && locationIds.Contains(x.StorageLocationId)
                && skuIds.Contains(x.StockKeepingUnitId))
            .ToArrayAsync(ct);
        var keys = originals.SelectMany(x => new[]
            { x.SourceStorageLocationId, x.DestinationStorageLocationId }
            .OfType<Guid>().Select(locationId =>
                (LocationId: locationId, SkuId: x.StockKeepingUnitId, x.ExpirationDate)))
            .ToHashSet();
        var balances = candidates.Where(x => keys.Contains(
            (x.StorageLocationId, x.StockKeepingUnitId, x.ExpirationDate))).ToArray();
        var skuId = lpn.Items.FirstOrDefault(x => !x.IsDeleted)?.StockKeepingUnitId;
        var zeroPreviouslyConfirmed = skuId is Guid id
            && await db.WarehouseAuditEvents.AnyAsync(x =>
                x.ReceivingOrderId == orderId && x.StockKeepingUnitId == id
                && x.EventType == "receiving.order.zero-confirmed", ct);
        return new ReceivingLpnDeletionState(order, lpn, movements, balances,
            locations, zeroPreviouslyConfirmed);
    }
}

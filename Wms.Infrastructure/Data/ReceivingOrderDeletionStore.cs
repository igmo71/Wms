using Microsoft.EntityFrameworkCore;
using Wms.Application.CommandExecution;
using Wms.Application.Receiving;
using Wms.Domain.Enums;

namespace Wms.Data;

public sealed class ReceivingOrderDeletionStore(
    IDbContextFactory<ApplicationDbContext> contextFactory) : IReceivingOrderDeletionStore
{
    public async Task<ReceivingOrderDeletionState?> LoadPreviewAsync(
        Guid orderId, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        return await LoadAsync(db, orderId, ct);
    }

    public Task<ReceivingOrderDeletionState?> LoadForDeletionAsync(
        ICommandExecutionContext session, Guid orderId, CancellationToken ct) =>
        LoadAsync(session is CommandExecutionStore.Session commandSession
            ? commandSession.Db
            : throw new InvalidOperationException("Удаление ордера требует сессию команды."),
            orderId, ct);

    private static async Task<ReceivingOrderDeletionState?> LoadAsync(
        ApplicationDbContext db, Guid orderId, CancellationToken ct)
    {
        var order = await db.ReceivingOrders
            .Include(x => x.Items)
            .Include(x => x.SourceLinks)
            .Include(x => x.LicensePlateNumbers).ThenInclude(x => x.Items)
            .Include(x => x.LicensePlateNumbers).ThenInclude(x => x.Putaways)
            .AsSplitQuery().SingleOrDefaultAsync(x => x.Id == orderId, ct);
        if (order is null)
            return null;

        var movements = await db.InventoryMovements
            .Where(x => x.RecorderType == RecorderType.ReceivingOrder
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
        var zeroSkuIds = await db.WarehouseAuditEvents.AsNoTracking()
            .Where(x => x.ReceivingOrderId == orderId
                && x.EventType == "receiving.order.zero-confirmed"
                && x.StockKeepingUnitId != null)
            .Select(x => x.StockKeepingUnitId!.Value).Distinct().ToArrayAsync(ct);
        return new ReceivingOrderDeletionState(order, movements, balances, locations,
            zeroSkuIds.ToHashSet());
    }
}

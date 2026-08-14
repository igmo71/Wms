using Microsoft.EntityFrameworkCore;
using Wms.Data;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Application.Services.ShippingOrders;

public class PickingQueryService(IDbContextFactory<ApplicationDbContext> dbContextFactory)
{
    public async Task<List<InventoryMovement>> GetPickingMovementsAsync(
        Guid orderId,
        int lineNumber,
        CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        return await dbContext.InventoryMovements
            .AsNoTracking()
            .Include(x => x.SourceStorageLocation)
            .Include(x => x.StockKeepingUnit)
            .Where(x => x.PostedAtUtc == null
                && x.RecorderType == RecorderType.ShippingOrder
                && x.RecorderId == orderId
                && x.RecorderLineNumber == lineNumber)
            .OrderBy(x => x.CreatedAtUtc)
            .ToListAsync(ct);
    }
    public async Task<List<PickingSourceLocationAvailability>> GetAvailableSourceLocationsAsync(
        Guid orderId,
        int lineNumber,
        CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var order = await dbContext.ShippingOrders
            .AsNoTracking()
            .Include(x => x.Items)
                .ThenInclude(x => x.StockKeepingUnit)
            .FirstOrDefaultAsync(x => x.Id == orderId, ct);

        if (order is null)
            return [];

        var orderItem = order.Items.FirstOrDefault(x => x.LineNumber == lineNumber);

        if (orderItem is null)
            return [];

        var draftQuantities = await dbContext.InventoryMovements
            .AsNoTracking()
            .Where(x => x.PostedAtUtc == null
                && x.RecorderType == RecorderType.ShippingOrder
                && x.RecorderId == orderId
                && x.StockKeepingUnitId == orderItem.StockKeepingUnitId
                && x.SourceStorageLocationId != null)
            .GroupBy(x => x.SourceStorageLocationId!.Value)
            .Select(x => new { StorageLocationId = x.Key, Quantity = x.Sum(movement => movement.Quantity) })
            .ToDictionaryAsync(x => x.StorageLocationId, x => x.Quantity, ct);

        var balances = await dbContext.InventoryBalances
            .AsNoTracking()
            .Include(x => x.StorageLocation)
                .ThenInclude(x => x!.Zone)
            .Where(x =>
                x.WarehouseId == order.WarehouseId &&
                x.StockKeepingUnitId == orderItem.StockKeepingUnitId &&
                x.StorageLocationId != order.ShippingLocationId &&
                x.StorageLocation!.Zone!.Type == ZoneType.Storage &&
                x.Quantity > 0)
            .OrderBy(x => x.StorageLocation!.Name)
            .ToListAsync(ct);

        return balances
            .Select(x => new PickingSourceLocationAvailability
            {
                StorageLocation = x.StorageLocation!,
                StockKeepingUnit = orderItem.StockKeepingUnit!,
                PhysicalQuantity = x.Quantity,
                DraftQuantity = draftQuantities.GetValueOrDefault(x.StorageLocationId)
            })
            .ToList();
    }
}

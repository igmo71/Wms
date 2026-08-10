using Microsoft.EntityFrameworkCore;
using Wms.Data;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Application.Services;

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
            .Where(x => x.PostedAtUtc == null
                && x.RecorderType == RecorderType.ShippingOrder
                && x.RecorderId == orderId
                && x.RecorderLineNumber == lineNumber)
            .OrderBy(x => x.CreatedAtUtc)
            .ToListAsync(ct);
    }

    public async Task<List<InventoryBalance>> GetAvailableSourceLocationsAsync(
        Guid orderId,
        int lineNumber,
        CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var orderLine = await dbContext.ShippingOrderItems
            .AsNoTracking()
            .Where(x => x.ShippingOrderId == orderId && x.LineNumber == lineNumber)
            .Join(
                dbContext.ShippingOrders.AsNoTracking(),
                item => item.ShippingOrderId,
                order => order.Id,
                (item, order) => new
                {
                    order.WarehouseId,
                    order.ShippingLocationId,
                    item.StockKeepingUnitId
                })
            .FirstOrDefaultAsync(ct);

        if (orderLine is null)
            return [];

        return await dbContext.InventoryBalances
            .AsNoTracking()
            .Where(x => x.WarehouseId == orderLine.WarehouseId
                && x.StockKeepingUnitId == orderLine.StockKeepingUnitId
                && x.Quantity > 0
                && x.StorageLocationId != orderLine.ShippingLocationId)
            .OrderBy(x => x.StorageLocation!.Name)
            .ToListAsync(ct);
    }
}

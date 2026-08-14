using Microsoft.EntityFrameworkCore;
using Wms.Data;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Application.Services.ReceivingOrders;

public class PutawayQueryService(IDbContextFactory<ApplicationDbContext> dbContextFactory)
{
    public async Task<List<InventoryMovement>> GetMovementsAsync(
        Guid orderId,
        CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        return await dbContext.InventoryMovements
            .AsNoTracking()
            .Include(x => x.DestinationStorageLocation)
                .ThenInclude(x => x!.Zone)
            .Include(x => x.StockKeepingUnit)
            .Where(x => x.RecorderType == RecorderType.ReceivingOrder
                && x.RecorderId == orderId
                && x.SourceStorageLocationId != null
                && x.DestinationStorageLocationId != null)
            .OrderBy(x => x.CreatedAtUtc)
            .ToListAsync(ct);
    }

    public async Task<List<StorageLocation>> SearchDestinationsAsync(
        Guid warehouseId,
        string? searchText,
        CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var query = dbContext.StorageLocations
            .AsNoTracking()
            .Include(x => x.Zone)
            .Where(x => x.WarehouseId == warehouseId
                && !x.DeletionMark
                && !x.Zone!.DeletionMark
                && x.Zone.Type == ZoneType.Storage);

        if (!string.IsNullOrWhiteSpace(searchText))
            query = query.Where(x => x.Name!.Contains(searchText));

        return await query
            .OrderBy(x => x.Zone!.Name)
                .ThenBy(x => x.Name)
            .Take(10)
            .ToListAsync(ct);
    }
}

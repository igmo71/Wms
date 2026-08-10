using Microsoft.EntityFrameworkCore;
using Wms.Common;
using Wms.Data;
using Wms.Domain;

namespace Wms.Application.Services;

public class InventoryCountQueryService(IDbContextFactory<ApplicationDbContext> dbContextFactory)
{
    public async Task<InventoryCount?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        return await dbContext.InventoryCounts
            .AsNoTracking()
            .Include(x => x.Warehouse)
            .Include(x => x.Items)
                .ThenInclude(x => x.StorageLocation)
            .Include(x => x.Items)
                .ThenInclude(x => x.StockKeepingUnit)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
    }

    public async Task<ListResult<InventoryCount>> ListAsync(ListQuery listQuery, CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        IQueryable<InventoryCount> query = dbContext.InventoryCounts
            .AsNoTracking()
            .Include(x => x.Warehouse);

        var totalItems = await query.CountAsync(ct);

        query = listQuery.SortBy switch
        {
            "CreatedAtUtc" => listQuery.SortDescending ? query.OrderByDescending(x => x.CreatedAtUtc) : query.OrderBy(x => x.CreatedAtUtc),
            "PostedAtUtc" => listQuery.SortDescending ? query.OrderByDescending(x => x.PostedAtUtc) : query.OrderBy(x => x.PostedAtUtc),
            "Status" => listQuery.SortDescending ? query.OrderByDescending(x => x.Status) : query.OrderBy(x => x.Status),
            _ => query.OrderByDescending(x => x.CreatedAtUtc)
        };

        var items = await query
            .Skip(listQuery.Skip)
            .Take(listQuery.Take)
            .ToListAsync(ct);

        return new ListResult<InventoryCount>
        {
            Items = items,
            TotalItems = totalItems
        };
    }
}

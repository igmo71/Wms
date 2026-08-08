using Microsoft.EntityFrameworkCore;
using Wms.Common;
using Wms.Data;
using Wms.Domain;

namespace Wms.Application.Services;

public class StorageLocationService(IDbContextFactory<ApplicationDbContext> dbContextFactory)
{
    public async Task CreateOrUpdateAsync(StorageLocation item, CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var exists = await dbContext.StorageLocations.AnyAsync(x => x.Id == item.Id, ct);

        if (exists)
            dbContext.StorageLocations.Update(item);
        else
            dbContext.StorageLocations.Add(item);

        await dbContext.SaveChangesAsync(ct);
    }

    public async Task<int> MarkDeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        int rowsAffected = await dbContext.StorageLocations
            .Where(x => x.Id == id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(e => e.DeletionMark, true), ct);

        return rowsAffected;
    }

    public async Task<int> UnMarkDeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        int rowsAffected = await dbContext.StorageLocations
            .Where(x => x.Id == id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(e => e.DeletionMark, false), ct);

        return rowsAffected;
    }

    public async Task<StorageLocation?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var result = await dbContext.StorageLocations
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, ct);

        return result;
    }

    public async Task<ListResult<StorageLocation>> ListAsync(StorageLocationListQuery listQuery, CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        IQueryable<StorageLocation> query = dbContext.StorageLocations
            .AsNoTracking()
            .Include(x => x.Warehouse)
            .Include(x => x.Zone);

        if (listQuery.ExcludeDeleted)
            query = query.Where(x => x.DeletionMark == false);

        query = ApplySearch(query, listQuery);

        int totalItems = await query.CountAsync(ct);

        query = ApplySorting(query, listQuery.SortBy, listQuery.SortDescending);

        var items = await query
            .Skip(listQuery.Skip)
            .Take(listQuery.Take)
            .ToListAsync(ct);

        return new ListResult<StorageLocation>
        {
            Items = items,
            TotalItems = totalItems
        };
    }

    private static IQueryable<StorageLocation> ApplySearch(
        IQueryable<StorageLocation> query,
        StorageLocationListQuery listQuery)
    {
        if (listQuery.WarehouseId is Guid warehouseId)
            query = query.Where(x => x.WarehouseId == warehouseId);

        if (listQuery.ZoneId is Guid zoneId)
            query = query.Where(x => x.ZoneId == zoneId);

        if (!string.IsNullOrWhiteSpace(listQuery.SearchString))
        {
            query = query.Where(x => x.Name!.Contains(listQuery.SearchString));
        }

        return query;
    }

    private static IQueryable<StorageLocation> ApplySorting(IQueryable<StorageLocation> query, string? sortBy, bool sortDescending)
    {
        return sortBy switch
        {
            "Name" => sortDescending ? query.OrderByDescending(x => x.Name) : query.OrderBy(x => x.Name),
            _ => query.OrderByDescending(x => x.Name),
        };
    }
}


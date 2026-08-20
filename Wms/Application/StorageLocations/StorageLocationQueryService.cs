using Microsoft.EntityFrameworkCore;
using Wms.Common;
using Wms.Data;
using Wms.Domain;

namespace Wms.Application.StorageLocations;

public class StorageLocationQueryService(IDbContextFactory<ApplicationDbContext> dbContextFactory)
{
    public async Task<IReadOnlyList<StorageLocation>> GetTreeAsync(
        Guid zoneId,
        bool includeDeleted,
        CancellationToken ct = default)
    {
        await using ApplicationDbContext dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        IQueryable<StorageLocation> locations = dbContext.StorageLocations
            .AsNoTracking()
            .Include(x => x.Warehouse)
            .Include(x => x.Zone)
            .Where(x => x.ZoneId == zoneId);

        if (!includeDeleted)
        {
            locations = locations.Where(x => !x.DeletionMark);
        }

        return await locations.OrderBy(x => x.Code).ToListAsync(ct);
    }

    public async Task<ListResult<StorageLocation>> ListAsync(
        StorageLocationListQuery query,
        CancellationToken ct = default)
    {
        await using ApplicationDbContext dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        IQueryable<StorageLocation> locations = dbContext.StorageLocations
            .AsNoTracking()
            .Include(x => x.Warehouse)
            .Include(x => x.Zone);

        if (query.ExcludeDeleted)
        {
            locations = locations.Where(x => !x.DeletionMark);
        }

        if (query.ExcludeFolders)
        {
            locations = locations.Where(x => !x.IsFolder);
        }

        if (query.WarehouseId is Guid warehouseId)
        {
            locations = locations.Where(x => x.WarehouseId == warehouseId);
        }

        if (query.ZoneId is Guid zoneId)
        {
            locations = locations.Where(x => x.ZoneId == zoneId);
        }

        if (query.ZoneType is Domain.Enums.ZoneType zoneType)
        {
            locations = locations.Where(x => x.Zone!.Type == zoneType);
        }

        if (!string.IsNullOrWhiteSpace(query.SearchString))
        {
            locations = locations.Where(x => x.Name!.Contains(query.SearchString)
                || x.Code!.Contains(query.SearchString));
        }

        var totalItems = await locations.CountAsync(ct);
        locations = query.SortBy switch
        {
            "Name" => query.SortDescending
                ? locations.OrderByDescending(x => x.Name)
                : locations.OrderBy(x => x.Name),
            "Code" => query.SortDescending
                ? locations.OrderByDescending(x => x.Code)
                : locations.OrderBy(x => x.Code),
            _ => locations.OrderBy(x => x.Code)
        };

        return new ListResult<StorageLocation>
        {
            Items = await locations.Skip(query.Skip).Take(query.Take).ToListAsync(ct),
            TotalItems = totalItems
        };
    }
}

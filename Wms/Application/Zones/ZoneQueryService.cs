using Microsoft.EntityFrameworkCore;
using Wms.Common;
using Wms.Data;
using Wms.Domain;

namespace Wms.Application.Zones;

public class ZoneQueryService(IDbContextFactory<ApplicationDbContext> dbContextFactory)
{
    public async Task<ListResult<Zone>> ListAsync(
        ZoneListQuery query,
        CancellationToken ct = default)
    {
        await using ApplicationDbContext dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        IQueryable<Zone> zones = dbContext.Zones
            .AsNoTracking()
            .Include(x => x.Warehouse);

        if (query.ExcludeDeleted)
        {
            zones = zones.Where(x => !x.DeletionMark);
        }

        zones = ApplySearch(zones, query);
        var totalItems = await zones.CountAsync(ct);
        zones = ApplySorting(zones, query.SortBy, query.SortDescending);

        List<Zone> items = await zones
            .Skip(query.Skip)
            .Take(query.Take)
            .ToListAsync(ct);

        return new ListResult<Zone>
        {
            Items = items,
            TotalItems = totalItems
        };
    }

    private static IQueryable<Zone> ApplySearch(
        IQueryable<Zone> zones,
        ZoneListQuery query)
    {
        if (!string.IsNullOrWhiteSpace(query.SearchString))
        {
            zones = zones.Where(x => x.Name!.Contains(query.SearchString)
                || x.Code!.Contains(query.SearchString));
        }

        if (query.WarehouseId is Guid warehouseId)
        {
            zones = zones.Where(x => x.WarehouseId == warehouseId);
        }

        if (query.Type is Domain.Enums.ZoneType type)
        {
            zones = zones.Where(x => x.Type == type);
        }

        return zones;
    }

    private static IQueryable<Zone> ApplySorting(
        IQueryable<Zone> zones,
        string? sortBy,
        bool sortDescending)
    {
        return sortBy switch
        {
            "Name" => sortDescending
                ? zones.OrderByDescending(x => x.Name)
                : zones.OrderBy(x => x.Name),
            "Type" => sortDescending
                ? zones.OrderByDescending(x => x.Type)
                : zones.OrderBy(x => x.Type),
            _ => zones.OrderByDescending(x => x.Name)
        };
    }
}

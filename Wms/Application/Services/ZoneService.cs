using Microsoft.EntityFrameworkCore;
using Wms.Common;
using Wms.Data;
using Wms.Domain;

namespace Wms.Application.Services;

public class ZoneService(IDbContextFactory<ApplicationDbContext> dbContextFactory)
{
    public async Task<ServiceResult> CreateOrUpdateAsync(Zone item, CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        try
        {
            item.UpdateDetails(item.Code ?? string.Empty, item.Name ?? string.Empty, item.Type);
        }
        catch (ArgumentException ex)
        {
            return ServiceError.Invalid<Zone>(ex.Message);
        }

        if (await dbContext.Zones.AnyAsync(x => x.WarehouseId == item.WarehouseId
            && x.Code == item.Code && x.Id != item.Id, ct))
        {
            return ServiceError.Conflict<Zone>("В выбранном складе уже есть зона с таким кодом.");
        }

        var existing = await dbContext.Zones
            .FirstOrDefaultAsync(x => x.Id == item.Id, ct);

        if (existing is not null)
        {
            if (existing.WarehouseId != item.WarehouseId
                && await dbContext.StorageLocations.AnyAsync(x => x.ZoneId == item.Id, ct))
            {
                return ServiceError.Invalid<Zone>("Зону со складскими позициями нельзя перенести в другой склад.");
            }

            existing.Code = item.Code;
            existing.Name = item.Name;
            existing.DeletionMark = item.DeletionMark;
            existing.WarehouseId = item.WarehouseId;
            existing.Type = item.Type;
        }
        else
            dbContext.Zones.Add(item);

        await dbContext.SaveChangesAsync(ct);
        return ServiceResult.Success();
    }

    public async Task<int> MarkDeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        int rowsAffected = await dbContext.Zones
            .Where(x => x.Id == id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(e => e.DeletionMark, true), ct);

        return rowsAffected;
    }

    public async Task<int> UnMarkDeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        int rowsAffected = await dbContext.Zones
            .Where(x => x.Id == id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(e => e.DeletionMark, false), ct);

        return rowsAffected;
    }

    public async Task<Zone?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var result = await dbContext.Zones
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, ct);

        return result;
    }

    public async Task<ListResult<Zone>> ListAsync(ZoneListQuery listQuery, CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        IQueryable<Zone> query = dbContext.Zones
            .AsNoTracking()
            .Include(x => x.Warehouse);

        if (listQuery.ExcludeDeleted)
            query = query.Where(x => x.DeletionMark == false);

        query = ApplySearch(query, listQuery);

        int totalItems = await query.CountAsync(ct);

        query = ApplySorting(query, listQuery.SortBy, listQuery.SortDescending);

        var items = await query
            .Skip(listQuery.Skip)
            .Take(listQuery.Take)
            .ToListAsync(ct);

        return new ListResult<Zone>
        {
            Items = items,
            TotalItems = totalItems
        };
    }

    private static IQueryable<Zone> ApplySearch(IQueryable<Zone> query, ZoneListQuery listQuery)
    {
        if (!string.IsNullOrWhiteSpace(listQuery.SearchString))
            query = query.Where(x => x.Name!.Contains(listQuery.SearchString)
                || x.Code!.Contains(listQuery.SearchString));

        if (listQuery.WarehouseId is Guid warehouseId)
            query = query.Where(x => x.WarehouseId == warehouseId);

        if (listQuery.Type is Domain.Enums.ZoneType type)
            query = query.Where(x => x.Type == type);

        return query;
    }

    private static IQueryable<Zone> ApplySorting(IQueryable<Zone> query, string? sortBy, bool sortDescending)
    {
        return sortBy switch
        {
            "Name" => sortDescending ? query.OrderByDescending(x => x.Name) : query.OrderBy(x => x.Name),
            "Type" => sortDescending ? query.OrderByDescending(x => x.Type) : query.OrderBy(x => x.Type),
            _ => query.OrderByDescending(x => x.Name),
        };
    }
}

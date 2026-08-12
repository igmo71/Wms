using Microsoft.EntityFrameworkCore;
using Wms.Common;
using Wms.Data;
using Wms.Domain;

namespace Wms.Application.Services;

public class DeliveryDirectionService(IDbContextFactory<ApplicationDbContext> dbContextFactory)
{
    internal async Task CreateOrUpdateAsync(DeliveryDirection item, CancellationToken ct)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var exists = await dbContext.DeliveryDirections.AnyAsync(x => x.Id == item.Id, ct);

        if (exists)
            dbContext.DeliveryDirections.Update(item);
        else
            dbContext.DeliveryDirections.Add(item);

        await dbContext.SaveChangesAsync(ct);
    }

    public async Task<DeliveryDirection?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var result = await dbContext.DeliveryDirections
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, ct);

        return result;
    }

    public async Task<ListResult<DeliveryDirection>> ListAsync(ListQuery listQuery, CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        IQueryable<DeliveryDirection> query = dbContext.DeliveryDirections
                .AsNoTracking();

        if (listQuery.ExcludeDeleted)
            query = query.Where(x => x.DeletionMark == false);

        query = ApplySearch(query, listQuery.SearchString);

        int totalItems = await query.CountAsync(ct);

        query = ApplySorting(query, listQuery.SortBy, listQuery.SortDescending);

        var items = await query
            .Skip(listQuery.Skip)
            .Take(listQuery.Take)
            .ToListAsync(ct);

        return new ListResult<DeliveryDirection>
        {
            Items = items,
            TotalItems = totalItems
        };
    }

    public async Task<List<DeliveryDirection>> ListTreeAsync(string? searchString, bool includeDeleted, CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var items = await dbContext.DeliveryDirections
            .AsNoTracking()
            .Where(x => includeDeleted || !x.DeletionMark)
            .ToListAsync(ct);

        if (string.IsNullOrWhiteSpace(searchString))
            return items;

        var matchingIds = items
            .Where(x => x.Description?.Contains(searchString, StringComparison.OrdinalIgnoreCase) == true)
            .Select(x => x.Id)
            .ToHashSet();
        var itemsById = items.ToDictionary(x => x.Id);

        foreach (var matchingId in matchingIds.ToArray())
        {
            var parentId = itemsById[matchingId].ParentId;
            while (parentId is Guid id && itemsById.TryGetValue(id, out var parent) && matchingIds.Add(id))
                parentId = parent.ParentId;
        }

        return items.Where(x => matchingIds.Contains(x.Id)).ToList();
    }

    private static IQueryable<DeliveryDirection> ApplySearch(IQueryable<DeliveryDirection> query, string? searchString)
    {
        if (!string.IsNullOrWhiteSpace(searchString))
        {
            query = query.Where(x => x.Description!.Contains(searchString));
        }

        return query;
    }

    private static IQueryable<DeliveryDirection> ApplySorting(IQueryable<DeliveryDirection> query, string? sortBy, bool sortDescending)
    {
        return sortBy switch
        {
            "Description" => sortDescending ? query.OrderByDescending(x => x.Description) : query.OrderBy(x => x.Description),
            "Comment" => sortDescending ? query.OrderByDescending(x => x.Comment) : query.OrderBy(x => x.Comment),
            _ => query.OrderBy(x => x.Description),
        };
    }
}

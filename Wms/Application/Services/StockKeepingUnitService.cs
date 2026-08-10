using Microsoft.EntityFrameworkCore;
using Wms.Common;
using Wms.Data;
using Wms.Domain;

namespace Wms.Application.Services;

public class StockKeepingUnitService(IDbContextFactory<ApplicationDbContext> dbContextFactory)
{
    public async Task CreateOrUpdateAsync(StockKeepingUnit item, CancellationToken ct = default)
    {
        using var activity = AppTracing.StartActivity("StockKeepingUnit CreateOrUpdate", nameof(StockKeepingUnitService));

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var exists = await dbContext.StockKeepingUnits.AnyAsync(x => x.Id == item.Id, ct);

        if (exists)
            dbContext.StockKeepingUnits.Update(item);
        else
            dbContext.StockKeepingUnits.Add(item);

        await dbContext.SaveChangesAsync(ct);
    }

    public async Task CreateOrUpdateBatchAsync(List<StockKeepingUnit> items, CancellationToken ct = default)
    {
        using var activity = AppTracing.StartActivity("StockKeepingUnit CreateOrUpdateBatch", nameof(StockKeepingUnitService));

        if (items == null || items.Count == 0) return;

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var incomingIds = items.Select(x => x.Id).ToList();

        var existingIds = await dbContext.StockKeepingUnits
            .Where(x => incomingIds.Contains(x.Id))
            .Select(x => x.Id)
            .ToHashSetAsync(ct);

        foreach (var item in items)
        {
            if (existingIds.Contains(item.Id))
            {
                dbContext.StockKeepingUnits.Update(item);
            }
            else
            {
                dbContext.StockKeepingUnits.Add(item);
            }
        }
        await dbContext.SaveChangesAsync(ct);
    }

    public async Task<StockKeepingUnit?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var result = await dbContext.StockKeepingUnits
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, ct);

        return result;
    }

    public async Task<ListResult<StockKeepingUnit>> ListAsync(ListQuery listQuery, CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        IQueryable<StockKeepingUnit> query = dbContext.StockKeepingUnits
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

        return new ListResult<StockKeepingUnit>
        {
            Items = items,
            TotalItems = totalItems
        };
    }

    private static IQueryable<StockKeepingUnit> ApplySearch(IQueryable<StockKeepingUnit> query, string? searchString)
    {
        if (!string.IsNullOrWhiteSpace(searchString))
        {
            query = query.Where(x => x.Name!.Contains(searchString));
        }

        return query;
    }

    private static IQueryable<StockKeepingUnit> ApplySorting(IQueryable<StockKeepingUnit> query, string? sortBy, bool sortDescending)
    {
        return sortBy switch
        {
            "Name" => sortDescending ? query.OrderByDescending(x => x.Name) : query.OrderBy(x => x.Name),
            _ => query.OrderBy(x => x.Name),
        };
    }
}

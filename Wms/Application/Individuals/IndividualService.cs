using Microsoft.EntityFrameworkCore;
using Wms.Common;
using Wms.Data;
using Wms.Domain;

namespace Wms.Application.Individuals;

public class IndividualService(IDbContextFactory<ApplicationDbContext> dbContextFactory)
{
    public async Task CreateOrUpdateAsync(Individual item, CancellationToken ct = default)
    {
        using var activity = AppTracing.StartActivity("Individual CreateOrUpdate", nameof(IndividualService));

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var exists = await dbContext.Individuals.AnyAsync(x => x.Id == item.Id, ct);

        if (exists)
            dbContext.Individuals.Update(item);
        else
            dbContext.Individuals.Add(item);

        await dbContext.SaveChangesAsync(ct);
    }

    public async Task CreateOrUpdateBatchAsync(List<Individual> items, CancellationToken ct = default)
    {
        using var activity = AppTracing.StartActivity("Individual CreateOrUpdateBatch", nameof(IndividualService));

        if (items.Count == 0)
            return;

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var incomingIds = items.Select(x => x.Id).ToList();
        var existingIds = await dbContext.Individuals
            .Where(x => incomingIds.Contains(x.Id))
            .Select(x => x.Id)
            .ToHashSetAsync(ct);

        foreach (var item in items)
        {
            if (existingIds.Contains(item.Id))
                dbContext.Individuals.Update(item);
            else
                dbContext.Individuals.Add(item);
        }

        await dbContext.SaveChangesAsync(ct);
    }

    public async Task<ListResult<Individual>> ListAsync(ListQuery listQuery, CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        IQueryable<Individual> query = dbContext.Individuals.AsNoTracking();

        if (listQuery.ExcludeDeleted)
            query = query.Where(x => !x.DeletionMark);

        if (!string.IsNullOrWhiteSpace(listQuery.SearchString))
            query = query.Where(x => x.Name!.Contains(listQuery.SearchString));

        var totalItems = await query.CountAsync(ct);

        query = listQuery.SortBy switch
        {
            "Name" => listQuery.SortDescending
                ? query.OrderByDescending(x => x.Name)
                : query.OrderBy(x => x.Name),
            _ => query.OrderBy(x => x.Name)
        };

        var items = await query
            .Skip(listQuery.Skip)
            .Take(listQuery.Take)
            .ToListAsync(ct);

        return new ListResult<Individual>
        {
            Items = items,
            TotalItems = totalItems
        };
    }
}

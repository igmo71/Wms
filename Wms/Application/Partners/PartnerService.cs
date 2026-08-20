using Microsoft.EntityFrameworkCore;
using Wms.Common;
using Wms.Data;
using Wms.Domain;

namespace Wms.Application.Partners;

public class PartnerService(IDbContextFactory<ApplicationDbContext> dbContextFactory)
{
    public async Task CreateOrUpdateAsync(Partner item, CancellationToken ct = default)
    {
        using var activity = AppTracing.StartActivity("Partner CreateOrUpdate", nameof(PartnerService));

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var exists = await dbContext.Partners.AnyAsync(x => x.Id == item.Id, ct);

        if (exists)
            dbContext.Partners.Update(item);
        else
            dbContext.Partners.Add(item);

        await dbContext.SaveChangesAsync(ct);
    }

    public async Task CreateOrUpdateBatchAsync(List<Partner> items, CancellationToken ct = default)
    {
        using var activity = AppTracing.StartActivity("Partner CreateOrUpdateBatch", nameof(PartnerService));

        if (items.Count == 0)
            return;

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var incomingIds = items.Select(x => x.Id).ToList();
        var existingIds = await dbContext.Partners
            .Where(x => incomingIds.Contains(x.Id))
            .Select(x => x.Id)
            .ToHashSetAsync(ct);

        foreach (var item in items)
        {
            if (existingIds.Contains(item.Id))
                dbContext.Partners.Update(item);
            else
                dbContext.Partners.Add(item);
        }

        await dbContext.SaveChangesAsync(ct);
    }

    public async Task<ListResult<Partner>> ListAsync(ListQuery listQuery, CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        IQueryable<Partner> query = dbContext.Partners.AsNoTracking();

        if (listQuery.ExcludeDeleted)
            query = query.Where(x => !x.DeletionMark);

        if (!string.IsNullOrWhiteSpace(listQuery.SearchString))
            query = query.Where(x => x.Name!.Contains(listQuery.SearchString));

        var totalItems = await query.CountAsync(ct);

        query = listQuery.SortBy switch
        {
            "Code" => listQuery.SortDescending
                ? query.OrderByDescending(x => x.Code)
                : query.OrderBy(x => x.Code),
            "Name" => listQuery.SortDescending
                ? query.OrderByDescending(x => x.Name)
                : query.OrderBy(x => x.Name),
            _ => query.OrderBy(x => x.Name)
        };

        var items = await query
            .Skip(listQuery.Skip)
            .Take(listQuery.Take)
            .ToListAsync(ct);

        return new ListResult<Partner>
        {
            Items = items,
            TotalItems = totalItems
        };
    }
}

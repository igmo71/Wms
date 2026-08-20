using Microsoft.EntityFrameworkCore;
using Wms.Common;
using Wms.Data;
using Wms.Domain;

namespace Wms.Application.OrganizationalUnits;

public class OrganizationalUnitService(IDbContextFactory<ApplicationDbContext> dbContextFactory)
{
    public async Task CreateOrUpdateAsync(OrganizationalUnit item, CancellationToken ct = default)
    {
        using var activity = AppTracing.StartActivity(
            "OrganizationalUnit CreateOrUpdate",
            nameof(OrganizationalUnitService));

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var exists = await dbContext.OrganizationalUnits.AnyAsync(x => x.Id == item.Id, ct);

        if (exists)
            dbContext.OrganizationalUnits.Update(item);
        else
            dbContext.OrganizationalUnits.Add(item);

        await dbContext.SaveChangesAsync(ct);
    }

    public async Task CreateOrUpdateBatchAsync(List<OrganizationalUnit> items, CancellationToken ct = default)
    {
        using var activity = AppTracing.StartActivity(
            "OrganizationalUnit CreateOrUpdateBatch",
            nameof(OrganizationalUnitService));

        if (items.Count == 0)
            return;

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var incomingIds = items.Select(x => x.Id).ToList();
        var existingIds = await dbContext.OrganizationalUnits
            .Where(x => incomingIds.Contains(x.Id))
            .Select(x => x.Id)
            .ToHashSetAsync(ct);

        foreach (var item in items)
        {
            if (existingIds.Contains(item.Id))
                dbContext.OrganizationalUnits.Update(item);
            else
                dbContext.OrganizationalUnits.Add(item);
        }

        await dbContext.SaveChangesAsync(ct);
    }

    public async Task<ListResult<OrganizationalUnit>> ListAsync(
        ListQuery listQuery,
        CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        IQueryable<OrganizationalUnit> query = dbContext.OrganizationalUnits.AsNoTracking();

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

        return new ListResult<OrganizationalUnit>
        {
            Items = items,
            TotalItems = totalItems
        };
    }
}

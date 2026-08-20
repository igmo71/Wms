using Microsoft.EntityFrameworkCore;
using Wms.Common;
using Wms.Data;
using Wms.Domain;

namespace Wms.Application.UnitsOfMeasure;

public class UnitOfMeasureService(IDbContextFactory<ApplicationDbContext> dbContextFactory)
{
    public async Task CreateOrUpdateAsync(UnitOfMeasure item, CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var exists = await dbContext.UnitsOfMeasure.AnyAsync(x => x.Id == item.Id, ct);

        if (exists)
            dbContext.UnitsOfMeasure.Update(item);
        else
            dbContext.UnitsOfMeasure.Add(item);

        await dbContext.SaveChangesAsync(ct);
    }

    public async Task<UnitOfMeasure?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var result = await dbContext.UnitsOfMeasure
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, ct);

        return result;
    }

    public async Task<ListResult<UnitOfMeasure>> ListAsync(ListQuery listQuery, CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        IQueryable<UnitOfMeasure> query = dbContext.UnitsOfMeasure
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

        return new ListResult<UnitOfMeasure>
        {
            Items = items,
            TotalItems = totalItems
        };
    }

    private static IQueryable<UnitOfMeasure> ApplySearch(IQueryable<UnitOfMeasure> query, string? searchString)
    {
        if (!string.IsNullOrWhiteSpace(searchString))
        {
            query = query.Where(x => x.Name!.Contains(searchString));
        }

        return query;
    }

    private static IQueryable<UnitOfMeasure> ApplySorting(IQueryable<UnitOfMeasure> query, string? sortBy, bool sortDescending)
    {
        return sortBy switch
        {
            "Code" => sortDescending ? query.OrderByDescending(x => x.Code) : query.OrderBy(x => x.Code),
            "Abbreviation" => sortDescending ? query.OrderByDescending(x => x.Abbreviation) : query.OrderBy(x => x.Abbreviation),
            "Description" => sortDescending ? query.OrderByDescending(x => x.Description) : query.OrderBy(x => x.Description),
            "Numerator" => sortDescending ? query.OrderByDescending(x => x.Numerator) : query.OrderBy(x => x.Numerator),
            "Denominator" => sortDescending ? query.OrderByDescending(x => x.Denominator) : query.OrderBy(x => x.Denominator),
            "Name" => sortDescending ? query.OrderByDescending(x => x.Name) : query.OrderBy(x => x.Name),
            _ => query.OrderBy(x => x.Name),
        };
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wms.Common;
using Wms.Data;
using Wms.Domain;

namespace Wms.Application;

internal class UnitOfMeasureService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    ILogger<UnitOfMeasureService> logger)
{
    public async Task CreateOrUpdateAsync(UnitOfMeasure item, CancellationToken ct = default)
    {
        int updatedRows = await UpdateAsync(item, ct);

        if (updatedRows == 0)
        {
            await CreateAsync(item, ct);
        }
    }

    private async Task<UnitOfMeasure> CreateAsync(UnitOfMeasure item, CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var entity = dbContext.Set<UnitOfMeasure>().Add(item).Entity;

        _ = await dbContext.SaveChangesAsync(ct);

        if (logger.IsEnabled(LogLevel.Debug))
            logger.LogDebug("{Source} {@Entity}", nameof(CreateAsync), entity);

        return entity;
    }

    private async Task<int> UpdateAsync(UnitOfMeasure item, CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        int rowsAffected = await dbContext.UnitsOfMeasure
            .Where(x => x.Id == item.Id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(e => e.Abbreviation, item.Abbreviation)
                .SetProperty(e => e.Code, item.Code)
                .SetProperty(e => e.DeletionMark, item.DeletionMark)
                .SetProperty(e => e.Description, item.Description)
                .SetProperty(e => e.Name, item.Name)
                .SetProperty(e => e.Numerator, item.Numerator)
                .SetProperty(e => e.Denominator, item.Denominator), ct);

        if (logger.IsEnabled(LogLevel.Debug))
            logger.LogDebug("{Source} {@Entity}", nameof(UpdateAsync), item);

        return rowsAffected;
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
            "Name" => sortDescending ? query.OrderByDescending(x => x.Name) : query.OrderBy(x => x.Name),
            _ => query.OrderByDescending(x => x.Name),
        };
    }
}

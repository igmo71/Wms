using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wms.Common;
using Wms.Data;
using Wms.Domain;

namespace Wms.Application;

internal class StockKeepingUnitService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    ILogger<StockKeepingUnitService> logger)
{
    public async Task CreateOrUpdateAsync(StockKeepingUnit item, CancellationToken ct = default)
    {
        int updatedRows = await UpdateAsync(item, ct);

        if (updatedRows == 0)
        {
            await CreateAsync(item, ct);
        }
    }

    private async Task<StockKeepingUnit> CreateAsync(StockKeepingUnit item, CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var entity = dbContext.StockKeepingUnits.Add(item).Entity;

        try
        {
            await dbContext.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            logger.LogWarning("{Source} {Id} {DbUpdateException}", nameof(CreateAsync), item.Id, ex.Message);

            await UpdateAsync(item, ct); // TODO: Наверное нужно убрать
        }

        if (logger.IsEnabled(LogLevel.Debug))
            logger.LogDebug("{Source} {@Entity}", nameof(CreateAsync), entity);

        return entity;
    }

    private async Task<int> UpdateAsync(StockKeepingUnit item, CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        int rowsAffected = await dbContext.StockKeepingUnits
            .Where(x => x.Id == item.Id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(e => e.DeletionMark, item.DeletionMark)
                .SetProperty(e => e.Code, item.Code)
                .SetProperty(e => e.Name, item.Name)
                .SetProperty(e => e.BaseUnitOfMeasureId, item.BaseUnitOfMeasureId)
                .SetProperty(e => e.WeightKg, item.WeightKg), ct);

        if (logger.IsEnabled(LogLevel.Debug))
            logger.LogDebug("{Source} {@Entity}", nameof(UpdateAsync), item);

        return rowsAffected;
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
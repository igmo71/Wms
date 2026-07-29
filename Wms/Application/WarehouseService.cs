using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wms.Common;
using Wms.Data;
using Wms.Domain;

namespace Wms.Application;

internal class WarehouseService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    ILogger<WarehouseService> logger)
{
    public async Task CreateOrUpdateAsync(Warehouse item, CancellationToken ct = default)
    {
        int updatedRows = await UpdateAsync(item, ct);

        if (updatedRows == 0)
        {
            await CreateAsync(item, ct);
        }
    }

    private async Task<Warehouse> CreateAsync(Warehouse item, CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var entity = dbContext.Warehouses.Add(item).Entity;

        _ = await dbContext.SaveChangesAsync(ct);

        if (logger.IsEnabled(LogLevel.Debug))
            logger.LogDebug("{Source} {@Entity}", nameof(CreateAsync), entity);

        return entity;
    }

    private async Task<int> UpdateAsync(Warehouse item, CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        int rowsAffected = await dbContext.Warehouses
            .Where(x => x.Id == item.Id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(e => e.Name, item.Name)
                .SetProperty(e => e.DeletionMark, item.DeletionMark), ct);

        if (logger.IsEnabled(LogLevel.Debug))
            logger.LogDebug("{Source} {@Entity}", nameof(UpdateAsync), item);

        return rowsAffected;
    }

    public async Task<Warehouse?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var result = await dbContext.Warehouses
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, ct);

        return result;
    }

    public async Task<ListResult<Warehouse>> ListAsync(ListQuery listQuery, CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        IQueryable<Warehouse> query = dbContext.Warehouses
                .AsNoTracking();

        query = ApplySearch(query, listQuery.SearchString);

        int totalItems = await query.CountAsync(ct);

        query = ApplySorting(query, listQuery.SortBy, listQuery.SortDescending);

        var items = await query
            .Skip(listQuery.Skip)
            .Take(listQuery.Take)
            .ToListAsync(ct);

        return new ListResult<Warehouse>
        {
            Items = items,
            TotalItems = totalItems
        };
    }

    private static IQueryable<Warehouse> ApplySearch(IQueryable<Warehouse> query, string? searchString)
    {
        if (!string.IsNullOrWhiteSpace(searchString))
        {
            query = query.Where(x => x.Name!.Contains(searchString));
        }

        return query;
    }

    private static IQueryable<Warehouse> ApplySorting(IQueryable<Warehouse> query, string? sortBy, bool sortDescending)
    {
        return sortBy switch
        {
            "Name" => sortDescending ? query.OrderByDescending(x => x.Name) : query.OrderBy(x => x.Name),
            _ => query.OrderByDescending(x => x.Name),
        };
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wms.Common;
using Wms.Data;
using Wms.Domain;

namespace Wms.Application;

internal class StorageLocationService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    ILogger<StorageLocationService> logger)
{
    public async Task CreateOrUpdateAsync(StorageLocation item, CancellationToken ct = default)
    {
        int updatedRows = await UpdateAsync(item, ct);

        if (updatedRows == 0)
        {
            await CreateAsync(item, ct);
        }
    }

    private async Task<StorageLocation> CreateAsync(StorageLocation item, CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var entity = dbContext.StorageLocations.Add(item).Entity;

        _ = await dbContext.SaveChangesAsync(ct);

        if (logger.IsEnabled(LogLevel.Debug))
            logger.LogDebug("{Source} {@Entity}", nameof(CreateAsync), entity);

        return entity;
    }

    private async Task<int> UpdateAsync(StorageLocation item, CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        int rowsAffected = await dbContext.StorageLocations
            .Where(x => x.Id == item.Id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(e => e.Name, item.Name)
                .SetProperty(e => e.DeletionMark, item.DeletionMark), ct);

        if (logger.IsEnabled(LogLevel.Debug))
            logger.LogDebug("{Source} {@Entity}", nameof(UpdateAsync), item);

        return rowsAffected;
    }

    public async Task<StorageLocation?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var result = await dbContext.StorageLocations
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, ct);

        return result;
    }

    public async Task<ListResult<StorageLocation>> ListAsync(ListQuery listQuery, CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        IQueryable<StorageLocation> query = dbContext.StorageLocations
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

        return new ListResult<StorageLocation>
        {
            Items = items,
            TotalItems = totalItems
        };
    }

    private static IQueryable<StorageLocation> ApplySearch(IQueryable<StorageLocation> query, string? searchString)
    {
        if (!string.IsNullOrWhiteSpace(searchString))
        {
            query = query.Where(x => x.Name!.Contains(searchString));
        }

        return query;
    }

    private static IQueryable<StorageLocation> ApplySorting(IQueryable<StorageLocation> query, string? sortBy, bool sortDescending)
    {
        return sortBy switch
        {
            "Name" => sortDescending ? query.OrderByDescending(x => x.Name) : query.OrderBy(x => x.Name),
            _ => query.OrderByDescending(x => x.Name),
        };
    }

    public async Task<int> MarkDeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        int rowsAffected = await dbContext.StorageLocations
            .Where(x => x.Id == id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(e => e.DeletionMark, true), ct);

        if (logger.IsEnabled(LogLevel.Debug))
            logger.LogDebug("{Source} {@EntityId}", nameof(MarkDeleteAsync), id);

        return rowsAffected;
    }

    public async Task<int> UnMarkDeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        int rowsAffected = await dbContext.StorageLocations
            .Where(x => x.Id == id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(e => e.DeletionMark, false), ct);

        if (logger.IsEnabled(LogLevel.Debug))
            logger.LogDebug("{Source} {@EntityId}", nameof(UnMarkDeleteAsync), id);

        return rowsAffected;
    }
}


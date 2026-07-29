using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wms.Data;
using Wms.Domain;

namespace Wms.Application;

internal class WarehouseService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    ILogger<WarehouseService> logger)
{
    public async Task CreateOrUpdateAsync(Warehouse item, CancellationToken ct)
    {
        int updatedRows = await UpdateAsync(item);

        if (updatedRows == 0)
        {
            await CreateAsync(item);
        }
    }

    private async Task<Warehouse> CreateAsync(Warehouse item)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync();

        var entity = dbContext.Set<Warehouse>().Add(item).Entity;

        _ = await dbContext.SaveChangesAsync();

        if (logger.IsEnabled(LogLevel.Debug))
            logger.LogDebug("{Source} {@Entity}", nameof(CreateAsync), entity);

        return entity;
    }

    private async Task<int> UpdateAsync(Warehouse item)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync();

        int rowsAffected = await dbContext.Warehouses
            .Where(x => x.Id == item.Id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(e => e.Name, item.Name)
                .SetProperty(e => e.DeletionMark, item.DeletionMark));

        if (logger.IsEnabled(LogLevel.Debug))
            logger.LogDebug("{Source} {@Entity}", nameof(UpdateAsync), item);

        return rowsAffected;
    }
}

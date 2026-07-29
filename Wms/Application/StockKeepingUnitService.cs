using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wms.Data;
using Wms.Domain;

namespace Wms.Application;

internal class StockKeepingUnitService(ApplicationDbContext dbContext, ILogger<StockKeepingUnitService> logger)
{
    public async Task<StockKeepingUnit> CreateAsync(StockKeepingUnit item)
    {
        var entity = dbContext.Set<StockKeepingUnit>().Add(item).Entity;

        _ = await dbContext.SaveChangesAsync();

        if (logger.IsEnabled(LogLevel.Debug))
            logger.LogDebug("{Source} {@Entity}", nameof(CreateAsync), entity);

        return entity;
    }

    public async Task<StockKeepingUnit?> UpdateAsync(StockKeepingUnit item)
    {
        await dbContext.StockKeepingUnits
            .Where(x => x.Id == item.Id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(e => e.DeletionMark, item.DeletionMark)
                .SetProperty(e => e.Code, item.Code)
                .SetProperty(e => e.DeletionMark, item.DeletionMark)
                .SetProperty(e => e.Description, item.Description)
                .SetProperty(e => e.Name, item.Name)
                .SetProperty(e => e.IsFolder, item.IsFolder)
                .SetProperty(e => e.ParentId, item.ParentId)
                .SetProperty(e => e.BaseUnitOfMeasureId, item.BaseUnitOfMeasureId)
                .SetProperty(e => e.WeightKg, item.WeightKg));

        if (logger.IsEnabled(LogLevel.Debug))
            logger.LogDebug("{Source} {@Entity}", nameof(UpdateAsync), item);

        return item;
    }

    public async Task CreateOrUpdateAsync(StockKeepingUnit item, CancellationToken ct)
    {
        var exists = await dbContext.StockKeepingUnits.AnyAsync(x => x.Id == item.Id);
        if (exists)
        {
            await UpdateAsync(item);
        }
        else
        {
            await CreateAsync(item);
        }
    }

    public async Task CreateOrUpdateListAsync(IEnumerable<StockKeepingUnit> items, CancellationToken ct)
    {
        foreach (var item in items)
        {
            await CreateOrUpdateAsync(item, ct);
        }
    }
}

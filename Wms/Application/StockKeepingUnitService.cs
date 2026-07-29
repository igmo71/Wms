using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wms.Data;
using Wms.Domain;

namespace Wms.Application;

internal class StockKeepingUnitService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    ILogger<StockKeepingUnitService> logger)
{
    public async Task<StockKeepingUnit> CreateAsync(StockKeepingUnit item)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync();

        var entity = dbContext.Set<StockKeepingUnit>().Add(item).Entity;

        try
        {
            await dbContext.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            logger.LogWarning("Параллельный поток успел вставить ID {Id}. Выполняем обновление.", item.Id);
            await UpdateAsync(item);
        }

        return entity;
    }

    public async Task<int> UpdateAsync(StockKeepingUnit item)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync();

        int rowsAffected = await dbContext.StockKeepingUnits
            .Where(x => x.Id == item.Id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(e => e.DeletionMark, item.DeletionMark)
                .SetProperty(e => e.Code, item.Code)
                .SetProperty(e => e.Name, item.Name)
                .SetProperty(e => e.IsFolder, item.IsFolder)
                .SetProperty(e => e.ParentId, item.ParentId)
                .SetProperty(e => e.BaseUnitOfMeasureId, item.BaseUnitOfMeasureId)
                .SetProperty(e => e.WeightKg, item.WeightKg));

        return rowsAffected;
    }

    public async Task CreateOrUpdateAsync(StockKeepingUnit item, CancellationToken ct)
    {
        int updatedRows = await UpdateAsync(item);

        if (updatedRows == 0)
        {
            await CreateAsync(item);
        }
    }
}
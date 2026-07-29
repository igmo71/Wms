using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wms.Data;
using Wms.Domain;

namespace Wms.Application;

public class UnitOfMeasureService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    ILogger<UnitOfMeasureService> logger)
{
    public async Task<UnitOfMeasure> CreateAsync(UnitOfMeasure item)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync();

        var entity = dbContext.Set<UnitOfMeasure>().Add(item).Entity;

        _ = await dbContext.SaveChangesAsync();

        if (logger.IsEnabled(LogLevel.Debug))
            logger.LogDebug("{Source} {@Entity}", nameof(CreateAsync), entity);

        return entity;
    }

    public async Task<int> UpdateAsync(UnitOfMeasure item)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync();

        int rowsAffected = await dbContext.UnitsOfMeasure
            .Where(x => x.Id == item.Id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(e => e.Abbreviation, item.Abbreviation)
                .SetProperty(e => e.Code, item.Code)
                .SetProperty(e => e.DeletionMark, item.DeletionMark)
                .SetProperty(e => e.Description, item.Description)
                .SetProperty(e => e.Name, item.Name)
                .SetProperty(e => e.Numerator, item.Numerator)
                .SetProperty(e => e.Denominator, item.Denominator));

        if (logger.IsEnabled(LogLevel.Debug))
            logger.LogDebug("{Source} {@Entity}", nameof(UpdateAsync), item);

        return rowsAffected;
    }

    public async Task CreateOrUpdateAsync(UnitOfMeasure item, CancellationToken ct)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync();

        int updatedRows = await UpdateAsync(item);

        if (updatedRows == 0)
        {
            await CreateAsync(item);
        }
    }
}

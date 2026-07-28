using Microsoft.EntityFrameworkCore;
using Wms.WebApp.Data;
using Wms.WebApp.Domain;

namespace Wms.WebApp.Application;

public class UnitOfMeasureService(ApplicationDbContext dbContext, ILogger<UnitOfMeasureService> logger)
{
    public async Task<UnitOfMeasure> CreateAsync(UnitOfMeasure item)
    {
        var entity = dbContext.Set<UnitOfMeasure>().Add(item).Entity;

        _ = await dbContext.SaveChangesAsync();

        logger.LogDebug("{Source} {@Entity}", nameof(CreateAsync), entity);

        return entity;
    }

    public async Task<UnitOfMeasure?> UpdateAsync(UnitOfMeasure item)
    {
        var existing = dbContext.Set<UnitOfMeasure>().FirstOrDefault(e => e.Id == item.Id);

        if (existing is null)
            return null;

        existing.Abbreviation = item.Abbreviation;
        existing.Code = existing.Code;
        existing.DeletionMark = item.DeletionMark;
        existing.Description = item.Description;
        existing.Name = item.Name;
        existing.Numerator = item.Numerator;
        existing.Denominator = item.Denominator;

        _ = await dbContext.SaveChangesAsync();

        logger.LogDebug("{Source} {@Entity}", nameof(UpdateAsync), existing);

        return existing;
    }

    internal async Task CreateOrUpdateAsync(UnitOfMeasure item, CancellationToken ct)
    {
        var exists = await dbContext.UnitsOfMeasure.AnyAsync(e => e.Id == item.Id, ct);
        if (exists)
        {
            await UpdateAsync(item);
        }
        else
        {
            await CreateAsync(item);
        }
    }
}

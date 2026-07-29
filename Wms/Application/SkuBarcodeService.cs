using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wms.Data;
using Wms.Domain;

namespace Wms.Application;

internal class SkuBarcodeService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    ILogger<SkuBarcodeService> logger)
{

    public async Task<SkuBarcode> CreateAsync(SkuBarcode item, CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var entity = dbContext.SkuBarcodes.Add(item).Entity;

        _ = await dbContext.SaveChangesAsync(ct);

        if (logger.IsEnabled(LogLevel.Debug))
            logger.LogDebug("{Source} {@Entity}", nameof(CreateAsync), entity);

        return entity;
    }

    public async Task DeleteAsync(SkuBarcode item, CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        await dbContext.SkuBarcodes.Where(x => x.SkuId == item.SkuId && x.Value == item.Value)
            .ExecuteDeleteAsync(ct);

        if (logger.IsEnabled(LogLevel.Debug))
            logger.LogDebug("{Source} {@Item}", nameof(DeleteAsync), item);
    }

    public async Task<int> CreateListAsync(List<SkuBarcode> items, CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        await dbContext.SkuBarcodes.AddRangeAsync(items, ct);

        var affected = await dbContext.SaveChangesAsync(ct);

        if (logger.IsEnabled(LogLevel.Debug))
            logger.LogDebug("{Source} {@Affected}", nameof(CreateListAsync), affected);

        return affected;
    }

    public async Task DeleteAllAsync(CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var affected = await dbContext.SkuBarcodes.ExecuteDeleteAsync(ct);

        if (logger.IsEnabled(LogLevel.Debug))
            logger.LogDebug("{Source} {@Affected}", nameof(DeleteAllAsync), affected);

    }

    public async Task<SkuBarcode?> GetAsync(string value, CancellationToken ct)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var result = await dbContext.SkuBarcodes.FirstOrDefaultAsync(x => x.Value == value, ct);

        return result;
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wms.Common;
using Wms.Data;
using Wms.Domain;

namespace Wms.Application.SkuBarcodes;

public class SkuBarcodeService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    ILogger<SkuBarcodeService> logger)
{
    public async Task<OperationResult<StockKeepingUnit>> ResolveAsync(
        string? barcode,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(barcode))
        {
            return OperationError.Invalid("Штрихкод товара не указан.");
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        var matches = await dbContext.SkuBarcodes
            .AsNoTracking()
            .Where(x => x.Value == barcode)
            .Select(x => x.Sku!)
            .Include(x => x.BaseUnitOfMeasure)
            .Take(2)
            .ToListAsync(ct);

        if (matches.Count == 0)
        {
            return OperationError.NotFound("Товар с таким штрихкодом не найден.");
        }

        if (matches.Count > 1)
        {
            return OperationError.Conflict("Штрихкод соответствует нескольким товарам.");
        }

        var sku = matches[0];
        if (sku.DeletionMark)
        {
            return OperationError.Invalid("Товар недоступен.");
        }

        return sku;
    }

    public async Task<ListResult<SkuBarcode>> ListAsync(ListQuery listQuery, CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        IQueryable<SkuBarcode> query = dbContext.SkuBarcodes
            .AsNoTracking()
            .Include(x => x.Sku);

        if (listQuery.ExcludeDeleted)
            query = query.Where(x => !x.Sku!.DeletionMark);

        if (!string.IsNullOrWhiteSpace(listQuery.SearchString))
        {
            var searchString = listQuery.SearchString;
            query = query.Where(x => x.Value!.Contains(searchString) || x.Sku!.Name!.Contains(searchString));
        }

        var totalItems = await query.CountAsync(ct);

        query = listQuery.SortBy switch
        {
            "Sku.Name" => listQuery.SortDescending ? query.OrderByDescending(x => x.Sku!.Name) : query.OrderBy(x => x.Sku!.Name),
            "Value" => listQuery.SortDescending ? query.OrderByDescending(x => x.Value) : query.OrderBy(x => x.Value),
            _ => query.OrderBy(x => x.Value)
        };

        var items = await query.Skip(listQuery.Skip).Take(listQuery.Take).ToListAsync(ct);

        return new ListResult<SkuBarcode> { Items = items, TotalItems = totalItems };
    }
    public async Task<int> CreateListAsync(List<SkuBarcode> items, CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        await dbContext.SkuBarcodes.AddRangeAsync(items, ct);

        var affected = await dbContext.SaveChangesAsync(ct);

        if (logger.IsEnabled(LogLevel.Debug))
            logger.LogDebug("{Source} {Created}", nameof(CreateListAsync), affected);

        return affected;
    }

    public async Task DeleteAllAsync(CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var affected = await dbContext.SkuBarcodes
            .ExecuteDeleteAsync(ct);

        if (logger.IsEnabled(LogLevel.Debug))
            logger.LogDebug("{Source} {Deleted}", nameof(DeleteAllAsync), affected);

    }

    public async Task DeleteRangeAsync(Guid skuId, CancellationToken ct)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var affected = await dbContext.SkuBarcodes
            .Where(x => x.SkuId == skuId)
            .ExecuteDeleteAsync(ct);

        if (logger.IsEnabled(LogLevel.Debug))
            logger.LogDebug("{Source} {Deleted} {SkuId}", nameof(DeleteRangeAsync), affected, skuId);
    }
}

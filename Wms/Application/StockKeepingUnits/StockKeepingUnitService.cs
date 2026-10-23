using Microsoft.EntityFrameworkCore;
using Wms.Common;
using Wms.Data;
using Wms.Domain;

namespace Wms.Application.StockKeepingUnits;

public class StockKeepingUnitService(IDbContextFactory<ApplicationDbContext> dbContextFactory)
{
    public async Task<OperationResult<StockKeepingUnit>> ResolveByBarcodeAsync(
        string? barcode,
        CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        return await ResolveByBarcodeAsync(dbContext, barcode, ct);
    }

    internal async Task<OperationResult<StockKeepingUnit>> ResolveByBarcodeAsync(
        ApplicationDbContext dbContext,
        string? barcode,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(barcode))
        {
            return OperationError.Invalid("Штрихкод товара не указан.");
        }

        var barcodeMatches = await dbContext.SkuBarcodes
            .AsNoTracking()
            .Include(x => x.Sku)
            .ThenInclude(x => x!.BaseUnitOfMeasure)
            .Where(x => x.Value == barcode)
            .Take(2)
            .ToListAsync(ct);

        var matches = barcodeMatches.Select(x => x.Sku!).ToList();

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

    public async Task CreateOrUpdateAsync(StockKeepingUnit item, CancellationToken ct = default)
    {
        using var activity = AppTracing.StartActivity("StockKeepingUnit CreateOrUpdate", nameof(StockKeepingUnitService));

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var existing = await dbContext.StockKeepingUnits
            .SingleOrDefaultAsync(x => x.Id == item.Id, ct);
        if (existing is null)
            dbContext.StockKeepingUnits.Add(item);
        else
            CopyImportedFields(existing, item);

        await dbContext.SaveChangesAsync(ct);
    }

    public async Task CreateOrUpdateBatchAsync(List<StockKeepingUnit> items, CancellationToken ct = default)
    {
        using var activity = AppTracing.StartActivity("StockKeepingUnit CreateOrUpdateBatch", nameof(StockKeepingUnitService));

        if (items == null || items.Count == 0) return;

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var incomingIds = items.Select(x => x.Id).ToList();

        var existing = await dbContext.StockKeepingUnits
            .Where(x => incomingIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);

        foreach (var item in items)
        {
            if (existing.TryGetValue(item.Id, out var current))
                CopyImportedFields(current, item);
            else
                dbContext.StockKeepingUnits.Add(item);
        }
        await dbContext.SaveChangesAsync(ct);
    }

    private static void CopyImportedFields(StockKeepingUnit target, StockKeepingUnit source)
    {
        target.Code = source.Code;
        target.Name = source.Name;
        target.DeletionMark = source.DeletionMark;
        target.BaseUnitOfMeasureId = source.BaseUnitOfMeasureId;
        target.WeightKg = source.WeightKg;
        target.VolumeM3 = source.VolumeM3;
        // TracksExpirationDate belongs to WMS and is never supplied by 1C.
    }

    public async Task<ListResult<StockKeepingUnit>> ListAsync(ListQuery listQuery, CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        IQueryable<StockKeepingUnit> query = dbContext.StockKeepingUnits
                .AsNoTracking()
                .Include(x => x.BaseUnitOfMeasure);

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
            "Code" => sortDescending ? query.OrderByDescending(x => x.Code) : query.OrderBy(x => x.Code),
            "BaseUnitOfMeasure.Name" => sortDescending
                ? query.OrderByDescending(x => x.BaseUnitOfMeasure!.Name)
                : query.OrderBy(x => x.BaseUnitOfMeasure!.Name),
            "WeightKg" => sortDescending ? query.OrderByDescending(x => x.WeightKg) : query.OrderBy(x => x.WeightKg),
            "VolumeM3" => sortDescending ? query.OrderByDescending(x => x.VolumeM3) : query.OrderBy(x => x.VolumeM3),
            "Name" => sortDescending
                ? query.OrderByDescending(x => x.Name).ThenBy(x => x.Code).ThenBy(x => x.Id)
                : query.OrderBy(x => x.Name).ThenBy(x => x.Code).ThenBy(x => x.Id),
            _ => query.OrderBy(x => x.Name).ThenBy(x => x.Code).ThenBy(x => x.Id),
        };
    }
}

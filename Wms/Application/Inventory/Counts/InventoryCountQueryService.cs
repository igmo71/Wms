using Microsoft.EntityFrameworkCore;
using Wms.Common;
using Wms.Data;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Application.Inventory.Counts;

public class InventoryCountQueryService(IDbContextFactory<ApplicationDbContext> dbContextFactory)
{
    public async Task<InventoryCount?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        return await dbContext.InventoryCounts
            .AsNoTracking()
            .Include(x => x.Warehouse)
            .Include(x => x.StorageLocation)
                .ThenInclude(x => x!.Zone)
            .Include(x => x.Items.OrderBy(x => x.LineNumber))
                .ThenInclude(x => x.StockKeepingUnit)
                    .ThenInclude(x => x!.BaseUnitOfMeasure)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
    }

    public async Task<ListResult<InventoryCount>> ListAsync(ListQuery listQuery, CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        IQueryable<InventoryCount> query = dbContext.InventoryCounts
            .AsNoTracking()
            .Include(x => x.Warehouse)
            .Include(x => x.StorageLocation)
                .ThenInclude(x => x!.Zone);

        var totalItems = await query.CountAsync(ct);

        query = listQuery.SortBy switch
        {
            "CreatedAtUtc" => listQuery.SortDescending ? query.OrderByDescending(x => x.CreatedAtUtc) : query.OrderBy(x => x.CreatedAtUtc),
            "Number" => listQuery.SortDescending ? query.OrderByDescending(x => x.Number) : query.OrderBy(x => x.Number),
            "Date" => listQuery.SortDescending ? query.OrderByDescending(x => x.Date) : query.OrderBy(x => x.Date),
            "PostedAtUtc" => listQuery.SortDescending ? query.OrderByDescending(x => x.PostedAtUtc) : query.OrderBy(x => x.PostedAtUtc),
            "Warehouse" or "Warehouse.Name" => listQuery.SortDescending ? query.OrderByDescending(x => x.Warehouse!.Name) : query.OrderBy(x => x.Warehouse!.Name),
            "Status" => listQuery.SortDescending ? query.OrderByDescending(x => x.Status) : query.OrderBy(x => x.Status),
            _ => query.OrderByDescending(x => x.CreatedAtUtc)
        };

        var items = await query
            .Skip(listQuery.Skip)
            .Take(listQuery.Take)
            .ToListAsync(ct);

        return new ListResult<InventoryCount>
        {
            Items = items,
            TotalItems = totalItems
        };
    }

    public async Task<IReadOnlyList<InventoryCount>> ListDraftsAsync(
        Guid warehouseId,
        int take,
        CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        return await dbContext.InventoryCounts
            .AsNoTracking()
            .Include(x => x.Warehouse)
            .Include(x => x.StorageLocation)
                .ThenInclude(x => x!.Zone)
            .Include(x => x.Items)
            .Where(x => x.WarehouseId == warehouseId && x.Status == InventoryCountStatus.Draft)
            .OrderByDescending(x => x.UpdatedAtUtc ?? x.CreatedAtUtc)
            .Take(Math.Clamp(take, 1, 100))
            .ToListAsync(ct);
    }

    public async Task<InventoryCount?> GetDraftByStorageLocationAsync(
        Guid storageLocationId,
        CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        return await dbContext.InventoryCounts
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.StorageLocationId == storageLocationId
                && x.Status == InventoryCountStatus.Draft,
                ct);
    }

    public async Task<OperationResult<IReadOnlyList<InventoryCountSkuSearchResult>>> SearchSkusAsync(
        Guid inventoryCountId,
        string searchText,
        int take,
        CancellationToken ct = default)
    {
        var term = searchText.Trim();
        if (term.Length < 2)
            return Array.Empty<InventoryCountSkuSearchResult>();

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        if (!await dbContext.InventoryCounts.AnyAsync(
            x => x.Id == inventoryCountId && x.Status == InventoryCountStatus.Draft,
            ct))
            return OperationError.NotFound($"Черновик инвентаризации '{inventoryCountId}' не найден.");

        var matches = await dbContext.StockKeepingUnits
            .AsNoTracking()
            .Where(x => !x.DeletionMark
                && ((x.Name != null && x.Name.Contains(term))
                    || (x.Code != null && x.Code.Contains(term))
                    || dbContext.SkuBarcodes.Any(barcode =>
                        barcode.SkuId == x.Id
                        && barcode.Value != null
                        && barcode.Value.Contains(term))))
            .Select(x => new
            {
                x.Id,
                Code = x.Code ?? string.Empty,
                Name = x.Name ?? string.Empty,
                UnitOfMeasure = x.BaseUnitOfMeasure == null
                    ? null
                    : x.BaseUnitOfMeasure.Description,
                IsExactMatch = (x.Code != null && x.Code == term)
                    || (x.Name != null && x.Name == term)
                    || dbContext.SkuBarcodes.Any(barcode =>
                        barcode.SkuId == x.Id && barcode.Value == term)
            })
            .OrderByDescending(x => x.IsExactMatch)
            .ThenBy(x => x.Name)
            .ThenBy(x => x.Code)
            .Take(Math.Clamp(take, 1, 100))
            .ToListAsync(ct);

        return matches.Select(x => new InventoryCountSkuSearchResult(
            x.Id,
            x.Code,
            x.Name,
            x.UnitOfMeasure,
            x.IsExactMatch)).ToList();
    }
}

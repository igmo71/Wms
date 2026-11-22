using Microsoft.EntityFrameworkCore;
using Wms.Application.Inventory.Counts;
using Wms.Common;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Data;

public class InventoryCountQueryService(IDbContextFactory<ApplicationDbContext> dbContextFactory)
{
    public async Task<IReadOnlyList<StorageLocation>> SearchLocationsAsync(Guid warehouseId,
        string? searchText, CancellationToken ct = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        var locations = db.StorageLocations.AsNoTracking().Include(x => x.Zone)
            .Where(x => x.WarehouseId == warehouseId && !x.DeletionMark && !x.IsFolder
                && !x.Warehouse!.DeletionMark && !x.Zone!.DeletionMark
                && (x.Zone.Type == ZoneType.Storage || x.Zone.Type == ZoneType.Reserve));
        if (!string.IsNullOrWhiteSpace(searchText))
        {
            var term = searchText.Trim();
            var normalized = StorageLocationAddress.Normalize(term);
            var code = normalized.IsSuccess ? normalized.Value! : term;
            locations = locations.Where(x => x.Name.Contains(term) || x.Code.Contains(code));
        }
        return await locations.OrderBy(x => x.Code).Take(10).ToListAsync(ct);
    }

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
            .Where(x => (!x.DeletionMark || dbContext.InventoryCountItems.Any(item =>
                    item.InventoryCountId == inventoryCountId && item.StockKeepingUnitId == x.Id))
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
                RequiresExpiration = x.TracksExpirationDate || dbContext.InventoryCountItems.Any(item =>
                    item.InventoryCountId == inventoryCountId && item.StockKeepingUnitId == x.Id
                    && item.ExpirationDate != null),
                IsExactMatch = (x.Code != null && x.Code == term)
                    || (x.Name != null && x.Name == term)
                    || dbContext.SkuBarcodes.Any(barcode =>
                        barcode.SkuId == x.Id && barcode.Value == term)
            })
            .OrderBy(x => x.Name)
            .ThenBy(x => x.Code)
            .ThenBy(x => x.Id)
            .Take(Math.Clamp(take, 1, 100))
            .ToListAsync(ct);

        return matches.Select(x => new InventoryCountSkuSearchResult(
            x.Id,
            x.Code,
            x.Name,
            x.UnitOfMeasure,
            x.IsExactMatch,
            x.RequiresExpiration)).ToList();
    }
}

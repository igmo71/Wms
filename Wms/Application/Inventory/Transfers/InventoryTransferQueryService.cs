using Microsoft.EntityFrameworkCore;
using Wms.Common;
using Wms.Data;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Application.Inventory.Transfers;

public class InventoryTransferQueryService(IDbContextFactory<ApplicationDbContext> dbContextFactory)
{
    public async Task<IReadOnlyList<InventoryTransfer>> ListActiveAsync(
        Guid warehouseId,
        int take,
        CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        return await dbContext.InventoryTransfers
            .AsNoTracking()
            .Include(x => x.Warehouse)
            .Where(x => x.WarehouseId == warehouseId
                && x.Status != InventoryTransferStatus.Completed)
            .OrderByDescending(x => x.UpdatedAtUtc ?? x.CreatedAtUtc)
            .Take(take)
            .ToListAsync(ct);
    }

    public async Task<InventoryTransfer?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        return await dbContext.InventoryTransfers
            .AsNoTracking()
            .Include(x => x.Warehouse)
            .Include(x => x.TransitStorageLocation)
                .ThenInclude(x => x!.Zone)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
    }

    public async Task<OperationResult<double>> GetAvailableDirectQuantityAsync(
        Guid transferId,
        Guid sourceStorageLocationId,
        Guid stockKeepingUnitId,
        CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var transfer = await dbContext.InventoryTransfers
            .AsNoTracking()
            .Where(x => x.Id == transferId)
            .Select(x => new { x.WarehouseId, x.Status })
            .FirstOrDefaultAsync(ct);
        if (transfer is null)
        {
            return OperationError.NotFound($"Перемещение '{transferId}' не найдено.");
        }

        if (transfer.Status == InventoryTransferStatus.Completed)
        {
            return OperationError.Invalid("Завершенное перемещение нельзя изменять.");
        }

        var sourceLocation = await dbContext.StorageLocations
            .AsNoTracking()
            .Include(x => x.Zone)
            .FirstOrDefaultAsync(x => x.Id == sourceStorageLocationId, ct);
        if (sourceLocation is null)
        {
            return OperationError.NotFound(
                $"Исходная складская позиция '{sourceStorageLocationId}' не найдена.");
        }

        if (sourceLocation.IsFolder
            || sourceLocation.DeletionMark
            || sourceLocation.Zone?.DeletionMark == true
            || sourceLocation.WarehouseId != transfer.WarehouseId
            || sourceLocation.Zone?.Type != ZoneType.Storage)
        {
            return OperationError.Invalid(
                "Исходная ячейка должна быть активной обычной ячейкой склада перемещения.");
        }

        return await dbContext.InventoryBalances
            .AsNoTracking()
            .Where(x => x.WarehouseId == transfer.WarehouseId
                && x.StorageLocationId == sourceStorageLocationId
                && x.StockKeepingUnitId == stockKeepingUnitId)
            .Select(x => (double?)x.Quantity)
            .SingleOrDefaultAsync(ct) ?? 0;
    }

    public async Task<OperationResult<IReadOnlyList<InventoryTransferSkuSearchResult>>>
        SearchAvailableDirectSkusAsync(
            Guid transferId,
            Guid sourceStorageLocationId,
            string searchText,
            int take,
            CancellationToken ct = default)
    {
        var term = searchText.Trim();
        if (term.Length < 2)
        {
            return Array.Empty<InventoryTransferSkuSearchResult>();
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var transfer = await dbContext.InventoryTransfers
            .AsNoTracking()
            .Where(x => x.Id == transferId)
            .Select(x => new { x.WarehouseId, x.Status })
            .FirstOrDefaultAsync(ct);
        if (transfer is null)
        {
            return OperationError.NotFound($"Перемещение '{transferId}' не найдено.");
        }

        if (transfer.Status == InventoryTransferStatus.Completed)
        {
            return OperationError.Invalid("Завершенное перемещение нельзя изменять.");
        }

        var sourceLocationIsValid = await dbContext.StorageLocations
            .AsNoTracking()
            .AnyAsync(x => x.Id == sourceStorageLocationId
                && !x.IsFolder
                && !x.DeletionMark
                && x.WarehouseId == transfer.WarehouseId
                && !x.Zone!.DeletionMark
                && x.Zone.Type == ZoneType.Storage,
                ct);
        if (!sourceLocationIsValid)
        {
            return OperationError.Invalid(
                "Исходная ячейка должна быть активной обычной ячейкой склада перемещения.");
        }

        var matches = await dbContext.InventoryBalances
            .AsNoTracking()
            .Where(x => x.WarehouseId == transfer.WarehouseId
                && x.StorageLocationId == sourceStorageLocationId
                && x.Quantity > 0
                && !x.StockKeepingUnit!.DeletionMark
                && ((x.StockKeepingUnit.Name != null
                        && x.StockKeepingUnit.Name.Contains(term))
                    || (x.StockKeepingUnit.Code != null
                        && x.StockKeepingUnit.Code.Contains(term))
                    || dbContext.SkuBarcodes.Any(barcode =>
                        barcode.SkuId == x.StockKeepingUnitId
                        && barcode.Value != null
                        && barcode.Value.Contains(term))))
            .Select(x => new
            {
                Id = x.StockKeepingUnitId,
                Code = x.StockKeepingUnit!.Code ?? string.Empty,
                Name = x.StockKeepingUnit.Name ?? string.Empty,
                UnitOfMeasure = x.StockKeepingUnit.BaseUnitOfMeasure == null
                    ? null
                    : x.StockKeepingUnit.BaseUnitOfMeasure.Description,
                AvailableQuantity = x.Quantity,
                IsExactMatch = (x.StockKeepingUnit.Code != null
                        && x.StockKeepingUnit.Code == term)
                    || (x.StockKeepingUnit.Name != null && x.StockKeepingUnit.Name == term)
                    || dbContext.SkuBarcodes.Any(barcode =>
                        barcode.SkuId == x.StockKeepingUnitId
                        && barcode.Value == term)
            })
            .OrderByDescending(x => x.IsExactMatch)
            .ThenBy(x => x.Name)
            .ThenBy(x => x.Code)
            .Take(Math.Clamp(take, 1, 10))
            .ToListAsync(ct);

        return matches
            .Select(x => new InventoryTransferSkuSearchResult(
                x.Id,
                x.Code,
                x.Name,
                x.UnitOfMeasure,
                x.AvailableQuantity,
                x.IsExactMatch))
            .ToList();
    }

    public async Task<InventoryMovement?> GetMovementAsync(
        Guid transferId,
        Guid movementId,
        CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        return await dbContext.InventoryMovements
            .AsNoTracking()
            .Include(x => x.SourceStorageLocation)
                .ThenInclude(x => x!.Zone)
            .Include(x => x.DestinationStorageLocation)
                .ThenInclude(x => x!.Zone)
            .Include(x => x.StockKeepingUnit)
                .ThenInclude(x => x!.BaseUnitOfMeasure)
            .SingleOrDefaultAsync(x => x.Id == movementId
                && x.RecorderType == RecorderType.InventoryTransfer
                && x.RecorderId == transferId,
                ct);
    }

    public async Task<ListResult<InventoryTransfer>> ListAsync(
        InventoryTransferListQuery listQuery,
        CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        IQueryable<InventoryTransfer> query = dbContext.InventoryTransfers
            .AsNoTracking()
            .Include(x => x.Warehouse)
            .Include(x => x.TransitStorageLocation);

        if (!string.IsNullOrWhiteSpace(listQuery.SearchString))
            query = query.Where(x => x.Number != null && x.Number.Contains(listQuery.SearchString));

        if (listQuery.WarehouseId is Guid warehouseId)
            query = query.Where(x => x.WarehouseId == warehouseId);

        if (listQuery.Status is InventoryTransferStatus status)
            query = query.Where(x => x.Status == status);

        if (listQuery.DateFrom is DateTime dateFrom)
            query = query.Where(x => x.Date >= dateFrom.Date);

        if (listQuery.DateTo is DateTime dateTo)
            query = query.Where(x => x.Date < dateTo.Date.AddDays(1));

        var totalItems = await query.CountAsync(ct);

        query = listQuery.SortBy switch
        {
            "Number" => listQuery.SortDescending
                ? query.OrderByDescending(x => x.Number)
                : query.OrderBy(x => x.Number),
            "Date" => listQuery.SortDescending
                ? query.OrderByDescending(x => x.Date)
                : query.OrderBy(x => x.Date),
            "Warehouse" or "Warehouse.Name" => listQuery.SortDescending
                ? query.OrderByDescending(x => x.Warehouse!.Name)
                : query.OrderBy(x => x.Warehouse!.Name),
            "TransitStorageLocation" or "TransitStorageLocation.Name" => listQuery.SortDescending
                ? query.OrderByDescending(x => x.TransitStorageLocation!.Name)
                : query.OrderBy(x => x.TransitStorageLocation!.Name),
            "Status" => listQuery.SortDescending
                ? query.OrderByDescending(x => x.Status)
                : query.OrderBy(x => x.Status),
            "CreatedAtUtc" => listQuery.SortDescending
                ? query.OrderByDescending(x => x.CreatedAtUtc)
                : query.OrderBy(x => x.CreatedAtUtc),
            "CompletedAtUtc" => listQuery.SortDescending
                ? query.OrderByDescending(x => x.CompletedAtUtc)
                : query.OrderBy(x => x.CompletedAtUtc),
            _ => query.OrderByDescending(x => x.CreatedAtUtc)
        };

        var items = await query
            .Skip(listQuery.Skip)
            .Take(listQuery.Take)
            .ToListAsync(ct);

        return new ListResult<InventoryTransfer>
        {
            Items = items,
            TotalItems = totalItems
        };
    }

    public async Task<List<InventoryMovement>> GetMovementsAsync(
        Guid transferId,
        CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        return await dbContext.InventoryMovements
            .AsNoTracking()
            .Include(x => x.SourceStorageLocation)
                .ThenInclude(x => x!.Zone)
            .Include(x => x.DestinationStorageLocation)
                .ThenInclude(x => x!.Zone)
            .Include(x => x.StockKeepingUnit)
                .ThenInclude(x => x!.BaseUnitOfMeasure)
            .Where(x => x.RecorderType == RecorderType.InventoryTransfer
                && x.RecorderId == transferId)
            .OrderBy(x => x.RecorderLineNumber)
            .ToListAsync(ct);
    }

    public async Task<List<InventoryTransferTransitBalance>> GetTransitBalancesAsync(
        Guid transferId,
        CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var transitStorageLocationId = await dbContext.InventoryTransfers
            .AsNoTracking()
            .Where(x => x.Id == transferId)
            .Select(x => x.TransitStorageLocationId)
            .FirstOrDefaultAsync(ct);

        if (transitStorageLocationId is null)
            return [];

        return await dbContext.InventoryBalances
            .AsNoTracking()
            .Include(x => x.StockKeepingUnit)
            .Where(x => x.StorageLocationId == transitStorageLocationId && x.Quantity > 0)
            .OrderBy(x => x.StockKeepingUnit!.Name)
            .Select(x => new InventoryTransferTransitBalance
            {
                StockKeepingUnit = x.StockKeepingUnit!,
                Quantity = x.Quantity
            })
            .ToListAsync(ct);
    }

    public async Task<List<InventoryTransferStorageLocationBalance>> GetStorageLocationBalancesAsync(
        Guid storageLocationId,
        CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        return await dbContext.InventoryBalances
            .AsNoTracking()
            .Include(x => x.StockKeepingUnit)
            .Where(x => x.StorageLocationId == storageLocationId && x.Quantity > 0)
            .OrderBy(x => x.StockKeepingUnit!.Name)
            .Select(x => new InventoryTransferStorageLocationBalance
            {
                StockKeepingUnit = x.StockKeepingUnit!,
                Quantity = x.Quantity
            })
            .ToListAsync(ct);
    }

    public async Task<List<StorageLocation>> GetAvailableTransitStorageLocationsAsync(
        Guid warehouseId,
        string? searchText,
        CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var activeTransitStorageLocationIds = dbContext.InventoryTransfers
            .Where(x => x.Status != InventoryTransferStatus.Completed
                && x.TransitStorageLocationId.HasValue)
            .Select(x => x.TransitStorageLocationId!.Value);

        IQueryable<StorageLocation> query = dbContext.StorageLocations
            .AsNoTracking()
            .Include(x => x.Zone)
            .Where(x => x.WarehouseId == warehouseId
                && !x.IsFolder
                && !x.DeletionMark
                && !x.Zone!.DeletionMark
                && x.Zone!.Type == ZoneType.Transit
                && !activeTransitStorageLocationIds.Contains(x.Id)
                && !dbContext.InventoryBalances.Any(balance => balance.StorageLocationId == x.Id && balance.Quantity > 0));

        if (!string.IsNullOrWhiteSpace(searchText))
            query = query.Where(x => x.Name!.Contains(searchText) || x.Code!.Contains(searchText));

        return await query
            .OrderBy(x => x.Name)
            .Take(10)
            .ToListAsync(ct);
    }
}

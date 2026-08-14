using Microsoft.EntityFrameworkCore;
using Wms.Common;
using Wms.Data;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Application.Services.Inventory;

public class InventoryTransferQueryService(IDbContextFactory<ApplicationDbContext> dbContextFactory)
{
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
            .Include(x => x.DestinationStorageLocation)
                .ThenInclude(x => x!.Zone)
            .Include(x => x.StockKeepingUnit)
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
                && !x.DeletionMark
                && x.Zone!.Type == ZoneType.Transit
                && !activeTransitStorageLocationIds.Contains(x.Id)
                && !dbContext.InventoryBalances.Any(balance => balance.StorageLocationId == x.Id && balance.Quantity > 0));

        if (!string.IsNullOrWhiteSpace(searchText))
            query = query.Where(x => x.Name!.Contains(searchText));

        return await query
            .OrderBy(x => x.Name)
            .Take(10)
            .ToListAsync(ct);
    }
}

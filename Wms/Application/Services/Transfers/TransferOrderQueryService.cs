using Microsoft.EntityFrameworkCore;
using Wms.Common;
using Wms.Data;
using Wms.Domain;

namespace Wms.Application.Services.Transfers;

public class TransferOrderQueryService(IDbContextFactory<ApplicationDbContext> dbContextFactory)
{
    public async Task<TransferOrder?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        return await dbContext.TransferOrders
            .AsNoTracking()
            .Include(x => x.Warehouse)
            .Include(x => x.TransitStorageLocation)
                .ThenInclude(x => x!.Zone)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
    }

    public async Task<ListResult<TransferOrder>> ListAsync(
        TransferOrderListQuery listQuery,
        CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        IQueryable<TransferOrder> query = dbContext.TransferOrders
            .AsNoTracking()
            .Include(x => x.Warehouse)
            .Include(x => x.TransitStorageLocation);

        if (!string.IsNullOrWhiteSpace(listQuery.SearchString))
            query = query.Where(x => x.Number != null && x.Number.Contains(listQuery.SearchString));

        if (listQuery.WarehouseId is Guid warehouseId)
            query = query.Where(x => x.WarehouseId == warehouseId);

        if (listQuery.Status is Domain.Enums.TransferOrderStatus status)
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
            _ => query.OrderByDescending(x => x.CreatedAtUtc)
        };

        var items = await query
            .Skip(listQuery.Skip)
            .Take(listQuery.Take)
            .ToListAsync(ct);

        return new ListResult<TransferOrder>
        {
            Items = items,
            TotalItems = totalItems
        };
    }
}

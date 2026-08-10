using Microsoft.EntityFrameworkCore;
using Wms.Common;
using Wms.Data;
using Wms.Domain;

namespace Wms.Application.Services;

public class ReceivingOrderQueryService(IDbContextFactory<ApplicationDbContext> dbContextFactory)
{
    public async Task<ReceivingOrder?> GetOrderAsync(Guid id, CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var order = await dbContext.ReceivingOrders
            .AsNoTracking()
            .Include(x => x.Warehouse)
            .Include(x => x.ReceivingLocation)
                .ThenInclude(x => x!.Zone)
            .FirstOrDefaultAsync(x => x.Id == id, ct);

        if (order is null)
            return null;

        order.Items = await dbContext.ReceivingOrderItems
            .AsNoTracking()
            .Include(x => x.StockKeepingUnit)
            .Where(x => x.ReceivingOrderId == id)
            .ToListAsync(ct);

        return order;
    }

    public async Task<ListResult<ReceivingOrder>> ListOrdersAsync(ReceivingOrderListQuery listQuery, CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        IQueryable<ReceivingOrder> query = dbContext.ReceivingOrders
            .Include(x => x.Warehouse)
            .AsNoTracking();

        query = ApplySearch(query, listQuery);

        int totalItems = await query.CountAsync(ct);

        query = ApplySorting(query, listQuery);

        var items = await query
            .Skip(listQuery.Skip)
            .Take(listQuery.Take)
            .ToListAsync(ct);

        return new ListResult<ReceivingOrder>
        {
            Items = items,
            TotalItems = totalItems
        };
    }

    private static IQueryable<ReceivingOrder> ApplySearch(IQueryable<ReceivingOrder> query, ReceivingOrderListQuery listQuery)
    {
        if (listQuery.ExcludeDeleted)
            query = query.Where(x => x.DeletionMark == false);

        if (listQuery.IncludePostedOnly)
            query = query.Where(x => x.Posted == true);

        if (!string.IsNullOrWhiteSpace(listQuery.SearchString))
            query = query.Where(x => x.Number!.Contains(listQuery.SearchString));

        if (listQuery.DateFrom is not null)
            query = query.Where(x => x.Date >= listQuery.DateFrom);

        if (listQuery.DateTo is not null)
            query = query.Where(x => x.Date < ((DateTime)listQuery.DateTo).AddDays(1));

        if (listQuery.Status is not null)
            query = query.Where(x => x.Status == listQuery.Status);

        if (listQuery.Queue is not null)
            query = query.Where(x => x.Queue == listQuery.Queue);

        return query;
    }

    private static IQueryable<ReceivingOrder> ApplySorting(IQueryable<ReceivingOrder> query, ReceivingOrderListQuery listQuery)
    {
        return listQuery.SortBy switch
        {
            "Number" => listQuery.SortDescending ? query.OrderByDescending(x => x.Number) : query.OrderBy(x => x.Number),
            "Date" => listQuery.SortDescending ? query.OrderByDescending(x => x.Date) : query.OrderBy(x => x.Date),
            "StartedAtUtc" => listQuery.SortDescending ? query.OrderByDescending(x => x.StartedAtUtc) : query.OrderBy(x => x.StartedAtUtc),
            "CompletedAtUtc" => listQuery.SortDescending ? query.OrderByDescending(x => x.CompletedAtUtc) : query.OrderBy(x => x.CompletedAtUtc),
            _ => query.OrderByDescending(x => x.Date),
        };
    }
}

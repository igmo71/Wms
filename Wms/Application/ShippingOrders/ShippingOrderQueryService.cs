using Microsoft.EntityFrameworkCore;
using Wms.Application.Parties;
using Wms.Common;
using Wms.Data;
using Wms.Domain;

namespace Wms.Application.ShippingOrders;

public class ShippingOrderQueryService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    PartyQueryService partyQueryService)
{
    public async Task<ShippingOrder?> GetOrderAsync(Guid id, CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var order = await dbContext.ShippingOrders
            .AsNoTracking()
            .AsSplitQuery()
            .Include(x => x.Warehouse)
            .Include(x => x.DeliveryDirection)
            .Include(x => x.ShippingLocation)
                .ThenInclude(x => x!.Zone)
            .Include(x => x.Items)
                .ThenInclude(x => x.StockKeepingUnit)
            .Include(x => x.BaseItems)
                .ThenInclude(x => x.StockKeepingUnit)
            .FirstOrDefaultAsync(x => x.Id == id, ct);

        if (order is null)
        {
            return null;
        }

        await PopulateReceiversAsync([order], ct);

        return order;
    }

    public async Task<ListResult<ShippingOrder>> ListOrdersAsync(ShippingOrderListQuery listQuery, CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        IQueryable<ShippingOrder> query = dbContext.ShippingOrders
            .AsNoTracking()
            .Include(x => x.Warehouse)
            .Include(x => x.DeliveryDirection);

        query = ApplySearch(query, listQuery);

        int totalItems = await query.CountAsync(ct);

        query = ApplySorting(query, listQuery);

        var items = await query
            .Skip(listQuery.Skip)
            .Take(listQuery.Take)
            .ToListAsync(ct);

        await PopulateReceiversAsync(items, ct);

        return new ListResult<ShippingOrder>
        {
            Items = items,
            TotalItems = totalItems
        };
    }

    private async Task PopulateReceiversAsync(
        IReadOnlyCollection<ShippingOrder> orders,
        CancellationToken ct)
    {
        var parties = await partyQueryService.GetManyAsync(
            orders.Select(x => new PartyReference(x.ReceiverId, x.ReceiverType)),
            ct);

        foreach (var order in orders)
        {
            parties.TryGetValue(
                new PartyReference(order.ReceiverId, order.ReceiverType),
                out var receiver);
            order.SetReceiver(receiver);
        }
    }

    private static IQueryable<ShippingOrder> ApplySearch(IQueryable<ShippingOrder> query, ShippingOrderListQuery listQuery)
    {
        if (listQuery.ExcludeDeleted)
        {
            query = query.Where(x => x.DeletionMark == false);
        }

        if (listQuery.IncludePostedOnly)
        {
            query = query.Where(x => x.Posted == true);
        }

        if (listQuery.WarehouseId is Guid warehouseId)
        {
            query = query.Where(x => x.WarehouseId == warehouseId);
        }

        if (!string.IsNullOrWhiteSpace(listQuery.SearchString))
        {
            query = query.Where(x => x.Number!.Contains(listQuery.SearchString));
        }

        if (listQuery.DateFrom is not null)
        {
            query = query.Where(x => x.Date >= listQuery.DateFrom);
        }

        if (listQuery.DateTo is not null)
        {
            query = query.Where(x => x.Date < ((DateTime)listQuery.DateTo).AddDays(1));
        }

        if (listQuery.Status is not null)
        {
            query = query.Where(x => x.Status == listQuery.Status);
        }

        if (listQuery.Queue is not null)
        {
            query = query.Where(x => x.Queue == listQuery.Queue);
        }

        if (listQuery.WarehouseOperation is not null)
        {
            query = query.Where(x => x.WarehouseOperation == listQuery.WarehouseOperation);
        }

        return query;
    }

    private static IQueryable<ShippingOrder> ApplySorting(IQueryable<ShippingOrder> query, ShippingOrderListQuery listQuery)
    {
        return listQuery.SortBy switch
        {
            "Number" => listQuery.SortDescending ? query.OrderByDescending(x => x.Number) : query.OrderBy(x => x.Number),
            "Date" => listQuery.SortDescending ? query.OrderByDescending(x => x.Date) : query.OrderBy(x => x.Date),
            "Warehouse" or "Warehouse.Name" => listQuery.SortDescending ? query.OrderByDescending(x => x.Warehouse!.Name) : query.OrderBy(x => x.Warehouse!.Name),
            "DeliveryDirection" or "DeliveryDirection.Description" => listQuery.SortDescending ? query.OrderByDescending(x => x.DeliveryDirection!.Description) : query.OrderBy(x => x.DeliveryDirection!.Description),
            "Status" => listQuery.SortDescending ? query.OrderByDescending(x => x.Status) : query.OrderBy(x => x.Status),
            "Queue" => listQuery.SortDescending ? query.OrderByDescending(x => x.Queue) : query.OrderBy(x => x.Queue),
            "PickingStartedAtUtc" => listQuery.SortDescending ? query.OrderByDescending(x => x.PickingStartedAtUtc) : query.OrderBy(x => x.PickingStartedAtUtc),
            "ReadyForShipmentAtUtc" => listQuery.SortDescending ? query.OrderByDescending(x => x.ReadyForShipmentAtUtc) : query.OrderBy(x => x.ReadyForShipmentAtUtc),
            "ShippedAtUtc" => listQuery.SortDescending ? query.OrderByDescending(x => x.ShippedAtUtc) : query.OrderBy(x => x.ShippedAtUtc),
            _ => query.OrderByDescending(x => x.Date)
        };
    }
}

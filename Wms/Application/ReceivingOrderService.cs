using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wms.Common;
using Wms.Data;
using Wms.Domain;
using Wms.Integration.OneS.Services;

namespace Wms.Application;

public class ReceivingOrderService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    Document_ПриходныйОрдерНаТовары_OutboundService outboundService,
    ILogger<ReceivingOrderService> logger)
{
    public async Task<ReceivingOrderDetails?> GetOrderAsync(Guid id, CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var result = await dbContext.ReceivingOrders
            .AsNoTracking()
            .Select(ReceivingOrderDetails.Projection)
            .FirstOrDefaultAsync(x => x.Id == id, ct);

        return result;
    }

    public async Task<ListResult<ReceivingOrder>> ListOrdersAsync(DocumentListQuery listQuery, CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        IQueryable<ReceivingOrder> query = dbContext.ReceivingOrders
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

    private static IQueryable<ReceivingOrder> ApplySearch(IQueryable<ReceivingOrder> query, DocumentListQuery listQuery)
    {
        if (listQuery.ExcludeDeleted)
            query = query.Where(x => x.DeletionMark == false);

        if (listQuery.IncludePostedOnly)
            query = query.Where(x => x.Posted == true);

        if (!string.IsNullOrWhiteSpace(listQuery.SearchString))
            query = query.Where(x => x.Number!.Contains(listQuery.SearchString));

        if (listQuery.DateFrom is not null)
            query = query.Where(x => x.DateTime >= listQuery.DateFrom);

        if (listQuery.DateTo is not null)
            query = query.Where(x => x.DateTime < ((DateTime)listQuery.DateTo).AddDays(1));

        if (listQuery.Status is not null)
            query = query.Where(x => x.Status == listQuery.Status);

        return query;
    }

    private static IQueryable<ReceivingOrder> ApplySorting(IQueryable<ReceivingOrder> query, DocumentListQuery listQuery)
    {
        return listQuery.SortBy switch
        {
            "Number" => listQuery.SortDescending ? query.OrderByDescending(x => x.Number) : query.OrderBy(x => x.Number),
            "DateTime" => listQuery.SortDescending ? query.OrderByDescending(x => x.DateTime) : query.OrderBy(x => x.DateTime),
            "StartedAtUtc" => listQuery.SortDescending ? query.OrderByDescending(x => x.StartedAtUtc) : query.OrderBy(x => x.StartedAtUtc),
            "CompletedAtUtc" => listQuery.SortDescending ? query.OrderByDescending(x => x.CompletedAtUtc) : query.OrderBy(x => x.CompletedAtUtc),
            _ => query.OrderByDescending(x => x.DateTime),
        };
    }

    internal async Task CreateOrUpdateImporttedOrderAsync(ReceivingOrder externalItem, CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var existsingItem = await dbContext.ReceivingOrders
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == externalItem.Id, ct);

        if (existsingItem is null)
        {
            await CreateOrderAsync(externalItem, ct);
        }
        else if (existsingItem.DataVersion != externalItem.DataVersion)
        {
            // TODO: Нужно проверить Нотфткацию после StartOrderAsync и CompleteOrderAsync
            if (existsingItem.StartedAtUtc is null && existsingItem.CompletedAtUtc is null)
            {
                await UpdateOrderAsImportAsync(externalItem, ct);
            }
            else
            {
                logger.LogWarning("{Source} {Number} {Id}, cannot update",
                    nameof(CreateOrUpdateImporttedOrderAsync), externalItem.Number, externalItem.Id);
            }
        }
    }

    private async Task<ReceivingOrder> CreateOrderAsync(ReceivingOrder item, CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var entity = dbContext.ReceivingOrders.Add(item).Entity;

        try
        {
            await dbContext.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            logger.LogWarning("{Source} {Id} {DbUpdateException}", nameof(CreateOrderAsync), item.Id, ex.Message);

            await UpdateOrderAsImportAsync(item, ct);
        }

        if (logger.IsEnabled(LogLevel.Debug))
            logger.LogDebug("{Source} {Number} {DateTime} {Id}",
                nameof(CreateOrderAsync), entity.Number, entity.DateTime, entity.Id);

        return entity;
    }

    private async Task UpdateOrderAsImportAsync(ReceivingOrder receivingOrder, CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var existingOrder = await dbContext.ReceivingOrders
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == receivingOrder.Id, ct);

        if (existingOrder is null)
        {
            logger.LogWarning("{Source} {Id} not found", nameof(UpdateOrderAsImportAsync), receivingOrder.Id);
            return;
        }

        existingOrder.BaseOrderId = receivingOrder.BaseOrderId;
        existingOrder.BaseOrderType = receivingOrder.BaseOrderType;
        existingOrder.Status = receivingOrder.Status;
        existingOrder.BusinessOperation = receivingOrder.BusinessOperation;
        existingOrder.WarehouseOperation = receivingOrder.WarehouseOperation;
        existingOrder.Comment = receivingOrder.Comment;
        existingOrder.Posted = receivingOrder.Posted;
        existingOrder.DeletionMark = receivingOrder.DeletionMark;
        existingOrder.DataVersion = receivingOrder.DataVersion;

        SynchronizeOrderItems(existingOrder.Items, receivingOrder.Items);

        await dbContext.SaveChangesAsync(ct);
    }

    private static void SynchronizeOrderItems(
    List<ReceivingOrderItem> existingOrderItems,
    IReadOnlyCollection<ReceivingOrderItem> externalOrderItems)
    {
        var externalByKey = externalOrderItems.ToDictionary(
            item => (item.ReceivingOrderId, item.LineNumber));

        existingOrderItems.RemoveAll(existing =>
            !externalByKey.ContainsKey(
                (existing.ReceivingOrderId, existing.LineNumber)));

        var existingByKey = existingOrderItems.ToDictionary(
            item => (item.ReceivingOrderId, item.LineNumber));

        foreach (var external in externalOrderItems)
        {
            var key = (external.ReceivingOrderId, external.LineNumber);

            if (existingByKey.TryGetValue(key, out var existing))
            {
                existing.StockKeepingUnitId = external.StockKeepingUnitId;
                existing.PlanQuantity = external.PlanQuantity;
            }
            else
            {
                existingOrderItems.Add(new ReceivingOrderItem
                {
                    ReceivingOrderId = external.ReceivingOrderId,
                    LineNumber = external.LineNumber,
                    StockKeepingUnitId = external.StockKeepingUnitId,
                    PlanQuantity = external.PlanQuantity,
                    FactQuantity = 0
                });
            }
        }
    }

    public async Task StartOrderAsync(Guid orderId, CancellationToken ct = default)
    {
        var outboundResult = await outboundService.StartOrderAsync(orderId, ct);

        if (outboundResult is null)
        {
            logger.LogError("{Source} Start Order failed", nameof(StartOrderAsync));
            return;
        }

        // TODO: Обновление должно прилететь по нотификации, надо проверять
        //await UpdateOrderAsImportAsync(outboundResult, ct); 
    }

    public async Task CompleteOrderAsync(Guid orderId, CancellationToken ct = default)
    {
        var outboundResult = await outboundService.CompleteOrderAsync(orderId, ct);

        if (outboundResult is null)
        {
            logger.LogError("{Source} Complete Order failed", nameof(CompleteOrderAsync));
            return;
        }

        // TODO: Обновление должно прилететь по нотификации,
        // но уже установлено StartedAt и UpdateOrderAsImportAsync не пропустит, надо проверять
        //await UpdateOrderAsImportAsync(outboundResult, ct); 
    }

    public async Task CompleteOrderAsync(ReceivingOrder order, CancellationToken ct = default)
    {
        if (order.HasPlanFactDifference)
        {
            var updateOrderItemsResult = await outboundService.UpdateOrderItemsAsync(order.Id, order.Items, ct);

            if (updateOrderItemsResult is null)
            {
                logger.LogError("{Source} Update Order Items failed", nameof(CompleteOrderAsync));
                return;
            }
        }

        await CompleteOrderAsync(order.Id, ct);
    }

    public async Task<int> UpdateOrderItemAsync(ReceivingOrderItem orderItem, CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var result = await dbContext.ReceivingOrderItems
            .Where(x => x.ReceivingOrderId == orderItem.ReceivingOrderId && x.LineNumber == orderItem.LineNumber)
            .ExecuteUpdateAsync(x => x
                .SetProperty(p => p.FactQuantity, orderItem.FactQuantity)
                .SetProperty(p => p.Comment, orderItem.Comment), ct);

        return result;
    }
}

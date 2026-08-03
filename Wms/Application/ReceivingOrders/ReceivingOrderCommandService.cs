using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wms.Data;
using Wms.Domain;
using Wms.Integration.OneS.Services;

namespace Wms.Application.ReceivingOrders;

public class ReceivingOrderCommandService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    Document_ПриходныйОрдерНаТовары_OutboundService outboundService,
    ILogger<ReceivingOrderCommandService> logger)
{


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
            // TODO: Нужно проверить Нотфткацию после StartOrderAsync и CompleteOrderAsync? 
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
            logger.LogWarning("{Source} {Id} {DbUpdateException}",
                nameof(CreateOrderAsync), item.Id, ex.Message);

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

    public async Task<bool> StartOrderAsync(Guid orderId, CancellationToken ct = default)
    {
        var outboundResult = await outboundService.StartOrderAsync(orderId, ct);

        if (outboundResult is null)
        {
            logger.LogError("{Source} Start Order failed", nameof(StartOrderAsync));
            return false;
        }

        // TODO: Обновление должно прилететь по нотификации, надо проверять
        //await UpdateOrderAsImportAsync(outboundResult, ct); 

        return true;
    }

    public async Task<bool> CompleteOrderAsync(Guid orderId, CancellationToken ct = default)
    {
        var outboundResult = await outboundService.CompleteOrderAsync(orderId, ct);

        if (outboundResult is null)
        {
            logger.LogError("{Source} Complete Order failed", nameof(CompleteOrderAsync));
            return false;
        }

        // TODO: Обновление должно прилететь по нотификации,
        // но уже установлено StartedAt и UpdateOrderAsImportAsync не пропустит, надо проверять
        //await UpdateOrderAsImportAsync(outboundResult, ct); 

        return true;
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

    public async Task<int> UpdateOrderItemFactQuantityAsync(
        Guid receivingOrderId, int lineNumber, double factQuantity, string? comment, CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var result = await dbContext.ReceivingOrderItems
            .Where(x => x.ReceivingOrderId == receivingOrderId && x.LineNumber == lineNumber)
            .ExecuteUpdateAsync(x => x
                .SetProperty(p => p.FactQuantity, factQuantity)
                .SetProperty(p => p.Comment, comment), ct);

        return result;
    }
}

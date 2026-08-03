using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wms.Common;
using Wms.Data;
using Wms.Domain;
using Wms.Integration.OneS.Services;

namespace Wms.Application.ReceivingOrders;

public class ReceivingOrderCommandService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    Document_ПриходныйОрдерНаТовары_OutboundService outboundService,
    ILogger<ReceivingOrderCommandService> logger)
{


    internal async Task CreateOrUpdateImporttedOrderAsync(ReceivingOrder externaOrder, CancellationToken ct = default)
    {
        var source = nameof(CreateOrUpdateImporttedOrderAsync);

        if (logger.IsEnabled(LogLevel.Debug))
            logger.LogDebug("{Source} Start {externaOrderId} {@externaOrder}", source, externaOrder.Id, externaOrder);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var existsingOrder = await dbContext.ReceivingOrders
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == externaOrder.Id, ct);

        if (existsingOrder is null)
        {
            var entity = dbContext.ReceivingOrders.Add(externaOrder).Entity;
        }
        else if (existsingOrder.DataVersion != externaOrder.DataVersion)
        {
            if (logger.IsEnabled(LogLevel.Debug))
                logger.LogDebug("{Source} DataVersion differ {OrderId}", source, existsingOrder.Id);

            if (existsingOrder.StartedAtUtc is null && existsingOrder.CompletedAtUtc is null)
            {
                if (logger.IsEnabled(LogLevel.Debug))
                    logger.LogDebug("{Source} StartedAtUtc and CompletedAtUtc is null {OrderId}", source, existsingOrder.Id);

                UpdateOrder(externaOrder, existsingOrder);
            }
            else
            {
                logger.LogWarning("{Source} {Number} {OrderId}, cannot update", source, externaOrder.Number, existsingOrder.Id);
            }
        }

        await dbContext.SaveChangesAsync(ct);

        if (logger.IsEnabled(LogLevel.Debug))
            logger.LogDebug("{Source} Ok {externaOrderId}", source, externaOrder.Id);
    }

    private void UpdateOrder(ReceivingOrder externaOrder, ReceivingOrder existsingOrder)
    {
        var source = nameof(UpdateOrder);

        if (logger.IsEnabled(LogLevel.Debug))
            logger.LogDebug("{Source} Start {OrderId} {existsingOrderStatus} {externaOrderStatus}",
                source, existsingOrder.Id, existsingOrder.Status.GetDisplayName(), externaOrder.Status.GetDisplayName());

        existsingOrder.BaseOrderId = externaOrder.BaseOrderId;
        existsingOrder.BaseOrderType = externaOrder.BaseOrderType;
        existsingOrder.Status = externaOrder.Status;
        existsingOrder.Queue = externaOrder.Queue;
        existsingOrder.BusinessOperation = externaOrder.BusinessOperation;
        existsingOrder.WarehouseOperation = externaOrder.WarehouseOperation;
        existsingOrder.Comment = externaOrder.Comment;
        existsingOrder.Posted = externaOrder.Posted;
        existsingOrder.DeletionMark = externaOrder.DeletionMark;
        existsingOrder.DataVersion = externaOrder.DataVersion;

        UpdateOrderItems(existsingOrder.Items, externaOrder.Items);

        if (logger.IsEnabled(LogLevel.Debug))
            logger.LogDebug("{Source} Ok {OrderId} {existsingOrderStatus} {externaOrderStatus}",
                source, existsingOrder.Id, existsingOrder.Status.GetDisplayName(), externaOrder.Status.GetDisplayName());
    }

    private static void UpdateOrderItems(
    List<ReceivingOrderItem> existingOrderItems,
    IReadOnlyCollection<ReceivingOrderItem> externalOrderItems)
    {
        var externalByKey = externalOrderItems
            .ToDictionary(item => (item.ReceivingOrderId, item.LineNumber));

        existingOrderItems
            .RemoveAll(existing => !externalByKey.ContainsKey((existing.ReceivingOrderId, existing.LineNumber)));

        var existingByKey = existingOrderItems
            .ToDictionary(item => (item.ReceivingOrderId, item.LineNumber));

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
        var source = nameof(StartOrderAsync);

        if (logger.IsEnabled(LogLevel.Debug))
            logger.LogDebug("{Source} Start {orderId}", source, orderId);

        var outboundResult = await outboundService.StartOrderAsync(orderId, ct);

        if (logger.IsEnabled(LogLevel.Debug))
            logger.LogDebug("{Source} {orderId} {@outboundResult}", source, orderId, outboundResult);

        if (outboundResult is null)
        {
            logger.LogError("{Source} Start Order failed", source);
            return false;
        }

        // TODO: Обновление должно прилететь по нотификации, надо проверять
        //await UpdateOrderAsImportAsync(outboundResult, ct); 

        if (logger.IsEnabled(LogLevel.Debug))
            logger.LogDebug("{Source} Ok {orderId}", source, orderId);

        return true;
    }

    public async Task<bool> CompleteOrderAsync(Guid orderId, CancellationToken ct = default)
    {
        var source = nameof(CompleteOrderAsync);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var existingOrder = await dbContext.ReceivingOrders
            .FirstOrDefaultAsync(x => x.Id == orderId, ct);


        if (existingOrder is null)
        {
            logger.LogError("{Source} Order Not Found {orderId}", source, orderId);
            return false;
        }

        if (existingOrder.HasPlanFactDifference)
        {
            var updateOrderItemsResult = await outboundService.UpdateOrderItemsAsync(existingOrder.Id, existingOrder.Items, ct);

            if (updateOrderItemsResult is null)
            {
                logger.LogError("{Source} Update Order Items failed", source);
                return false;
            }
        }

        var outboundResult = await outboundService.CompleteOrderAsync(orderId, ct);

        if (outboundResult is null)
        {
            logger.LogError("{Source} Complete Order failed", source);
            return false;
        }

        // TODO: Обновление должно прилететь по нотификации,
        // но уже установлено StartedAt и UpdateOrderAsImportAsync не пропустит, надо проверять
        //await UpdateOrderAsImportAsync(outboundResult, ct); 

        return true;
    }

    public async Task<int> UpdateOrderItemFactQuantityAsync(
        Guid receivingOrderId,
        int lineNumber,
        double factQuantity,
        string? comment,
        CancellationToken ct = default)
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

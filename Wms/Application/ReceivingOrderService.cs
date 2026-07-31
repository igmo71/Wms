using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wms.Data;
using Wms.Domain;
using Wms.Integration.OneS.Services;

namespace Wms.Application;

internal class ReceivingOrderService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    Document_ПриходныйОрдерНаТовары_OutboundService outboundService,
    ILogger<ReceivingOrderService> logger)
{
    internal async Task CreateOrUpdateImporttedOrder(ReceivingOrder externalItem, CancellationToken ct)
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
            if (existsingItem.StartedAtUtc is null && existsingItem.CompletedAtUtc is null)
            {
                await UpdateOrderByImportAsync(externalItem, ct);
            }
            else
            {
                logger.LogWarning("{Source} {Number} {Id}, cannot update",
                    nameof(CreateOrUpdateImporttedOrder), externalItem.Number, externalItem.Id);
            }
        }
    }

    private async Task<ReceivingOrder> CreateOrderAsync(ReceivingOrder item, CancellationToken ct)
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

            await UpdateOrderByImportAsync(item, ct);
        }

        if (logger.IsEnabled(LogLevel.Debug))
            logger.LogDebug("{Source} {@Entity}", nameof(CreateOrderAsync), entity);

        return entity;
    }

    private async Task UpdateOrderByImportAsync(ReceivingOrder receivingOrder, CancellationToken ct)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var existingOrder = await dbContext.ReceivingOrders
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == receivingOrder.Id, ct);

        if (existingOrder is null)
        {
            logger.LogWarning("{Source} {Id} not found", nameof(UpdateOrderByImportAsync), receivingOrder.Id);
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

    public async Task StartOrder(Guid orderId, CancellationToken ct)
    {
        var outboundResult = await outboundService.StartOrderAsync(orderId, ct);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);
    }

    public async Task CompleteOrder(Guid orderId, CancellationToken ct)
    {
        var outboundResult = await outboundService.CompleteOrderAsync(orderId, ct);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);
    }
}
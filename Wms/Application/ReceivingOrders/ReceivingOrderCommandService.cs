using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Wms.Common;
using Wms.Data;
using Wms.Domain;
using Wms.Integration.OneS.Services;

namespace Wms.Application.ReceivingOrders;

public class ReceivingOrderCommandService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    IOptions<WmsSettings> options,
    InventoryBalanceService inventoryBalanceService,
    InventoryTurnoverService inventoryTurnoverService,
    Document_ПриходныйОрдерНаТовары_OutboundService outboundService,
    ILogger<ReceivingOrderCommandService> logger)
{
    private readonly WmsSettings _wmsSettings = options.Value;

    public async Task CreateOrUpdateImportedOrderAsync(
    ReceivingOrder externalOrder,
    CancellationToken ct = default)
    {
        const string source = nameof(CreateOrUpdateImportedOrderAsync);

        if (logger.IsEnabled(LogLevel.Debug))
            logger.LogDebug("{Source} Start {ExternalOrderId} {@ExternalOrder}", source, externalOrder.Id, externalOrder);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var existingOrder = await dbContext.ReceivingOrders
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == externalOrder.Id, ct);

        var now = DateTimeOffset.UtcNow;

        if (existingOrder is null)
        {
            externalOrder.CreatedAtUtc = now;
            dbContext.ReceivingOrders.Add(externalOrder);
        }
        else
        {
            var hasExternalChanges = existingOrder.HasImportChanges(externalOrder);

            if (!hasExternalChanges)
            {
                if (logger.IsEnabled(LogLevel.Debug))
                    logger.LogDebug("{Source} No external changes {OrderId}", source, existingOrder.Id);

                return;
            }

            if (!existingOrder.AllowExternalUpdate(_wmsSettings))
            {
                existingOrder.MarkExternalChangeDetected(now);

                if (logger.IsEnabled(LogLevel.Debug))
                    logger.LogDebug("{Source} External changes detected, update blocked {OrderId}", source, existingOrder.Id);
            }
            else
            {
                existingOrder.UpdateFromImport(externalOrder);
                existingOrder.UpdatedAtUtc = now;
                existingOrder.ClearExternalChangeDetected();
            }
        }

        await dbContext.SaveChangesAsync(ct);

        logger.LogDebug("{Source} Ok {ExternalOrderId}", source, externalOrder.Id);
    }

    public async Task<bool> StartOrderAsync(Guid orderId, CancellationToken ct = default)
    {
        var source = nameof(StartOrderAsync);

        if (logger.IsEnabled(LogLevel.Debug))
            logger.LogDebug("{Source} Start {orderId}", source, orderId);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var externalOrder = await outboundService.StartOrderAsync(orderId, ct);

        if (externalOrder is null)
        {
            logger.LogError("{Source} Failed {orderId}", source, orderId);
            return false;
        }

        var existingOrder = await dbContext.ReceivingOrders
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == orderId, ct);

        if (existingOrder is null)
        {
            logger.LogError("{Source} Not Found {orderId}", source, orderId);
            return false;
        }

        existingOrder.Status = externalOrder.Status; // TODO: ReceivingOrderStatus.InProcess ?
        existingOrder.StartedAtUtc = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(ct);

        if (logger.IsEnabled(LogLevel.Debug))
            logger.LogDebug("{Source} Ok {orderId}", source, orderId);

        return true;
    }

    public async Task<bool> CompleteOrderAsync(Guid orderId, CancellationToken ct = default)
    {
        var source = nameof(CompleteOrderAsync);

        if (logger.IsEnabled(LogLevel.Debug))
            logger.LogDebug("{Source} Start {orderId}", source, orderId);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var existingOrder = await dbContext.ReceivingOrders
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == orderId, ct);


        if (existingOrder is null)
        {
            logger.LogError("{Source} Not Found {orderId}", source, orderId);
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

        var externalOrder = await outboundService.CompleteOrderAsync(orderId, ct);


        if (externalOrder is null)
        {
            logger.LogError("{Source} Failed", source);
            return false;
        }

        existingOrder.UpdateFromImport(externalOrder);
        existingOrder.Status = externalOrder.Status;
        existingOrder.CompletedAtUtc = DateTimeOffset.UtcNow;

        await inventoryTurnoverService.CreateAsync(existingOrder, dbContext, ct);
        await inventoryBalanceService.CreateOrUpdateAsync(existingOrder, dbContext, ct);

        await dbContext.SaveChangesAsync(ct);

        if (logger.IsEnabled(LogLevel.Debug))
            logger.LogDebug("{Source} Ok {orderId}", source, orderId);

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

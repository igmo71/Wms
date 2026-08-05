using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Wms.Common;
using Wms.Data;
using Wms.Domain;
using Wms.Domain.Enums;
using Wms.Integration.OneS.Services;

namespace Wms.Application.ReceivingOrders;

public class ReceivingOrderCommandService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    IOptions<WmsSettings> options,
    BalanceAndTurnoverService balanceAndTurnoverService,
    Document_ПриходныйОрдерНаТовары_OutboundService outboundService,
    ILogger<ReceivingOrderCommandService> logger)
{
    private readonly WmsSettings _wmsSettings = options.Value;

    public async Task CreateOrUpdateImportedOrderAsync(ReceivingOrder externalOrder, CancellationToken ct = default)
    {
        using var scope = logger.BeginScope("ReceivingOrder CreateOrUpdateImported  {externalOrderId}", externalOrder.Id);
        using var activity = AppTracing.StartActivity("ReceivingOrder.Import ", nameof(ReceivingOrderCommandService));

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
            var hasExternalChanges = existingOrder.HasExternalChanges(externalOrder);

            if (!hasExternalChanges)
            {
                logger.LogDebug("No external changes");

                return;
            }

            if (!existingOrder.AllowExternalUpdate(_wmsSettings))
            {
                existingOrder.ExternalChangeDetected = true;

                logger.LogDebug("External changes detected, update blocked");
            }
            else
            {
                existingOrder.UpdateFrom(externalOrder);

                existingOrder.UpdatedAtUtc = now;

                existingOrder.ExternalChangeDetected = false;
            }
        }

        await dbContext.SaveChangesAsync(ct);
    }

    public async Task<bool> StartOrderAsync(Guid orderId, CancellationToken ct = default)
    {
        using var scope = logger.BeginScope("ReceivingOrder Start {orderId}", orderId);
        using var activity = AppTracing.StartActivity("ReceivingOrder.Start", nameof(ReceivingOrderCommandService));

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var existingOrder = await dbContext.ReceivingOrders
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == orderId, ct);

        if (existingOrder is null)
        {
            logger.LogError("Not Found");
            return false;
        }

        if (existingOrder.Status != ReceivingOrderStatus.Pending)
        {
            logger.LogError("Only a pending receiving order can be started.");
            return false;
        }

        if (existingOrder.ReceivingLocationId is null)
        {
            logger.LogError("Receiving location must be specified before starting the order.");
            return false;
        }

        var externalOrder = await outboundService.StartOrderAsync(orderId, ct);

        if (externalOrder is null)
        {
            logger.LogError("Failed to start external order");
            return false;
        }

        existingOrder.Status = externalOrder.Status; // TODO: ReceivingOrderStatus.InProcess ?

        existingOrder.StartedAtUtc = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(ct);

        return true;
    }

    public async Task<bool> CompleteOrderAsync(Guid orderId, CancellationToken ct = default)
    {
        using var scope = logger.BeginScope("ReceivingOrder Complete {orderId}", orderId);
        using var activity = AppTracing.StartActivity("ReceivingOrder.Complete", nameof(ReceivingOrderCommandService));

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var existingOrder = await dbContext.ReceivingOrders
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == orderId, ct);

        if (existingOrder is null)
        {
            logger.LogError("Not Found");
            return false;
        }

        if (existingOrder.Status != ReceivingOrderStatus.InProcess)
        {
            logger.LogError("Only an in-progress receiving order can be completed.");
            return false;
        }

        if (existingOrder.ReceivingLocationId is null)
        {
            logger.LogError("Receiving location must be specified before completing the order.");
            return false;
        }

        if (existingOrder.HasPlanFactDifference)
        {
            var updateExternalItemsResult = await outboundService
                .UpdateDocumentItemsAsync(existingOrder.Id, existingOrder.Items, ct);

            if (updateExternalItemsResult is null)
            {
                logger.LogError("Failed to update external order items");
                return false;
            }
        }

        var externalOrder = await outboundService.CompleteOrderAsync(orderId, ct);

        if (externalOrder is null)
        {
            logger.LogError("Failed to complete external order");
            return false;
        }

        existingOrder.UpdateFrom(externalOrder);

        existingOrder.Status = externalOrder.Status; // TODO: ReceivingOrderStatus.Completed ?

        existingOrder.CompletedAtUtc = DateTimeOffset.UtcNow;

        await balanceAndTurnoverService.CreateOrUpdateAsync(existingOrder, dbContext, ct);

        await dbContext.SaveChangesAsync(ct);

        return true;
    }

    public async Task<int> UpdateOrderItemFactQuantityAsync(
        Guid receivingOrderId,
        int lineNumber,
        decimal factQuantity,
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

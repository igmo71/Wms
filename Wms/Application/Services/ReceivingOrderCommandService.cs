using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Wms.Common;
using Wms.Data;
using Wms.Domain;
using Wms.Integration.OneS.Services;

namespace Wms.Application.Services;

public class ReceivingOrderCommandService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    IOptions<WmsSettings> options,
    BalanceAndTurnoverService balanceAndTurnoverService,
    Document_ПриходныйОрдерНаТовары_OutboundService outboundService,
    ILogger<ReceivingOrderCommandService> logger)
{
    private readonly WmsSettings _wmsSettings = options.Value;

    public async Task ImportOrderAsync(ReceivingOrder externalOrder, CancellationToken ct = default)
    {
        using var scope = logger.BeginScope("ReceivingOrder Import {OrderId}", externalOrder.Id);

        using var activity = AppTracing.StartActivity("ReceivingOrder.Import", nameof(ReceivingOrderCommandService));

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var existingOrder = await dbContext.ReceivingOrders
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == externalOrder.Id, ct);

        var now = DateTimeOffset.UtcNow;

        if (existingOrder is null)
        {
            if (!externalOrder.AllowExternalCreate(_wmsSettings))
            {
                logger.LogDebug("External document status is completed, new order create not allowed");

                return;
            }

            externalOrder.CreatedAtUtc = now;

            dbContext.ReceivingOrders.Add(externalOrder);
        }
        else
        {
            var hasExternalChanges = existingOrder.HasExternalChanges(externalOrder);

            if (!hasExternalChanges)
            {
                logger.LogDebug("No external document changes detected");

                return;
            }

            if (!existingOrder.AllowExternalUpdate(_wmsSettings))
            // Что бы разрешить для статуса Complete, вероятно, потребуется доработка (откат BalanceAndTurnover...)
            {
                existingOrder.ExternalChangeDetected = true;

                logger.LogWarning("External document changes detected, order update not allowed");
            }
            else
            {
                existingOrder.UpdateOrder(externalOrder);

                existingOrder.UpdatedAtUtc = now;

                existingOrder.ExternalChangeDetected = false;
            }
        }

        await dbContext.SaveChangesAsync(ct);
    }

    public async Task<ServiceResult> StartOrderAsync(Guid orderId, string userId, CancellationToken ct = default)
    {
        using var scope = logger.BeginScope("ReceivingOrder Start {OrderId}", orderId);

        using var activity = AppTracing.StartActivity("ReceivingOrder.Start", nameof(ReceivingOrderCommandService));

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var existingOrder = await dbContext.ReceivingOrders
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == orderId, ct);

        if (existingOrder is null)
        {
            logger.LogError("Not Found");
            return ServiceError.NotFound<ReceivingOrder>();
        }

        var validationResult = existingOrder.ValidateToStart();

        if (!validationResult.IsSuccess)
        {
            logger.LogError("Validation to start failed: {ErrorMessage}", validationResult.Error?.Message);
            return validationResult;
        }

        var externalStartResult = await outboundService.StartOrderAsync(orderId, ct);

        if (!externalStartResult.IsSuccess)
        {
            logger.LogError("Failed to start external document: {ErrorMessage}", externalStartResult.Error?.Message);
            return ServiceError.Failure<ReceivingOrder>("Failed to start external document");
        }

        existingOrder.Start(userId);

        await dbContext.SaveChangesAsync(ct);

        return ServiceResult.Success();
    }

    public async Task<ServiceResult> CompleteOrderAsync(Guid orderId, string userId, CancellationToken ct = default)
    {
        using var scope = logger.BeginScope("ReceivingOrder Complete {OrderId}", orderId);

        using var activity = AppTracing.StartActivity("ReceivingOrder.Complete", nameof(ReceivingOrderCommandService));

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var existingOrder = await dbContext.ReceivingOrders
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == orderId, ct);

        if (existingOrder is null)
        {
            logger.LogError("Not Found");
            return ServiceError.NotFound<ReceivingOrder>();
        }

        var validationResult = existingOrder.ValidateToComplete();

        if (!validationResult.IsSuccess)
        {
            logger.LogError("Validation to complete failed: {ErrorMessage}", validationResult.Error?.Message);
            return validationResult;
        }

        existingOrder.Complete(userId);

        var balanceAndTurnoverResult = await balanceAndTurnoverService
            .CompleteReceivingOrder(existingOrder, dbContext, ct);

        if (!balanceAndTurnoverResult.IsSuccess)
            return balanceAndTurnoverResult;

        if (existingOrder.HasPlanFactDifference)
        {
            var externalItemsUpdateResult = await outboundService
                .UpdateDocumentItemsAsync(existingOrder.Id, existingOrder.Items, ct);

            if (!externalItemsUpdateResult.IsSuccess)
            {
                logger.LogError("Failed to update external order items: {ErrorMessage}", externalItemsUpdateResult.Error?.Message);
                return externalItemsUpdateResult;
            }
        }

        var externalCompletionResult = await outboundService.CompleteOrderAsync(orderId, ct);

        if (!externalCompletionResult.IsSuccess)
        {
            logger.LogError("Failed to complete external document: {ErrorMessage}", externalCompletionResult.Error?.Message);
            return ServiceError.Failure<ReceivingOrder>("Failed to complete external document");
        }

        await dbContext.SaveChangesAsync(ct);

        return ServiceResult.Success();
    }

    public async Task<ServiceResult> UpdateOrderItemFactQuantityAsync(
        Guid receivingOrderId,
        int lineNumber,
        double factQuantity,
        string? comment,
        CancellationToken ct = default)
    {
        if (factQuantity < 0)
        {
            return ServiceError.Invalid<ReceivingOrderItem>("Fact quantity cannot be negative.");
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var affected = await dbContext.ReceivingOrderItems
            .Where(x => x.ReceivingOrderId == receivingOrderId && x.LineNumber == lineNumber)
            .ExecuteUpdateAsync(x => x
                .SetProperty(p => p.FactQuantity, factQuantity)
                .SetProperty(p => p.Comment, comment), ct);

        if (affected == 0)
        {
            return ServiceError.NotFound<ReceivingOrderItem>();
        }

        return ServiceResult.Success();
    }

    public async Task<ServiceResult> SetReceivingLocationAsync(
        Guid receivingOrderId,
        Guid receivingLocationId,
        CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var affected = await dbContext.ReceivingOrders
            .Where(x => x.Id == receivingOrderId)
            .ExecuteUpdateAsync(x => x
                .SetProperty(p => p.ReceivingLocationId, receivingLocationId), ct);

        if (affected == 0)
            return ServiceError.NotFound<ReceivingOrder>();

        return ServiceResult.Success();
    }
}

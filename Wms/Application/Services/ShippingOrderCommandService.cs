using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Wms.Common;
using Wms.Data;
using Wms.Domain;
using Wms.Integration.OneS.Services;

namespace Wms.Application.Services;

internal class ShippingOrderCommandService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    IOptions<WmsSettings> options,
    BalanceAndTurnoverService balanceAndTurnoverService,
    Document_РасходныйОрдерНаТовары_OutboundService outboundService,
    ILogger<ShippingOrderCommandService> logger)
{
    private readonly WmsSettings _wmsSettings = options.Value;

    internal async Task ImportOrderAsync(ShippingOrder externalOrder, CancellationToken ct)
    {
        using var scope = logger.BeginScope("ShippingOrder Import {OrderId}", externalOrder.Id);

        using var activity = AppTracing.StartActivity("ShippingOrder.Import", nameof(ShippingOrderCommandService));

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var existingOrder = await dbContext.ShippingOrders
            .Include(x => x.Items)
            .Include(x => x.BaseItems)
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

            dbContext.ShippingOrders.Add(externalOrder);
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
        using var scope = logger.BeginScope("ShippingOrder Start {OrderId}", orderId);

        using var activity = AppTracing.StartActivity("ShippingOrder.Start", nameof(ShippingOrderCommandService));

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var existingOrder = await dbContext.ShippingOrders
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == orderId, ct);

        if (existingOrder is null)
        {
            logger.LogError("Not Found");
            return ServiceError.NotFound<ShippingOrder>();
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
            return ServiceError.Failure<ShippingOrder>("Failed to start external document");
        }

        existingOrder.Start(userId);

        await dbContext.SaveChangesAsync(ct);

        return ServiceResult.Success();
    }

    public async Task<ServiceResult> CompleteOrderAsync(Guid orderId, string userId, CancellationToken ct = default)
    {
        using var scope = logger.BeginScope("ShippingOrder Complete {OrderId}", orderId);

        using var activity = AppTracing.StartActivity("ShippingOrder.Complete", nameof(ShippingOrderCommandService));

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var existingOrder = await dbContext.ShippingOrders
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == orderId, ct);

        if (existingOrder is null)
        {
            logger.LogError("Not Found");
            return ServiceError.NotFound<ShippingOrder>();
        }

        var validationResult = existingOrder.ValidateToComplete();

        if (!validationResult.IsSuccess)
        {
            logger.LogError("Validation to complete failed: {ErrorMessage}", validationResult.Error?.Message);
            return validationResult;
        }

        existingOrder.Complete(userId);

        var balanceAndTurnoverResult = await balanceAndTurnoverService
            .CompleteShippingOrder(existingOrder, dbContext, ct);

        if (!balanceAndTurnoverResult.IsSuccess)
            return balanceAndTurnoverResult;

        if (existingOrder.HasPlanFactDifference)
        {
            var externalItemsUpdateResult = await outboundService
                .UpdateDocumentItemsAsync(existingOrder, ct);

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
            return ServiceError.Failure<ShippingOrder>("Failed to complete external document");
        }

        await dbContext.SaveChangesAsync(ct);

        return ServiceResult.Success();
    }
}

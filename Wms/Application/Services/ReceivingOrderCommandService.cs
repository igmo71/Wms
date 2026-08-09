using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wms.Common;
using Wms.Data;
using Wms.Domain;
using Wms.Domain.Enums;
using Wms.Integration.OneS.Services;

namespace Wms.Application.Services;

public class ReceivingOrderCommandService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    BalanceAndTurnoverService balanceAndTurnoverService,
    Document_ПриходныйОрдерНаТовары_OutboundService outboundService,
    ILogger<ReceivingOrderCommandService> logger)
{
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
            if (externalOrder.Status != ReceivingOrderStatus.ReadyForReceiving)
            {
                logger.LogWarning("External receiving order create is not allowed for status {Status}", externalOrder.Status);
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

            if (existingOrder.Status != ReceivingOrderStatus.ReadyForReceiving)
            {
                existingOrder.ExternalChangeDetected = true;

                logger.LogWarning("External receiving order changes conflict. Local status: {LocalStatus}, external status: {ExternalStatus}",
                    existingOrder.Status, externalOrder.Status);
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

    public async Task<ServiceResult> SetInReceivingAsync(Guid orderId, string userId, CancellationToken ct = default)
    {
        using var scope = logger.BeginScope("ReceivingOrder SetInReceiving {OrderId}", orderId);
        using var activity = AppTracing.StartActivity("ReceivingOrder.SetInReceiving", nameof(ReceivingOrderCommandService));

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var existingOrder = await dbContext.ReceivingOrders
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == orderId, ct);

        if (existingOrder is null)
        {
            logger.LogError("Not Found");
            return ServiceError.NotFound<ReceivingOrder>();
        }

        var validationResult = existingOrder.ValidateToSetInReceiving();

        if (!validationResult.IsSuccess)
        {
            logger.LogError("Validation to set in receiving failed: {ErrorMessage}", validationResult.Error?.Message);
            return validationResult;
        }

        var externalResult = await outboundService.SetInReceivingAsync(orderId, ct);

        if (!externalResult.IsSuccess)
        {
            logger.LogError("Failed to set external document in receiving: {ErrorMessage}", externalResult.Error?.Message);
            return externalResult;
        }

        existingOrder.SetInReceiving(userId);

        await dbContext.SaveChangesAsync(ct);

        return ServiceResult.Success();
    }

    public async Task<ServiceResult> SetReceivedAsync(Guid orderId, string userId, CancellationToken ct = default)
    {
        using var scope = logger.BeginScope("ReceivingOrder SetReceived {OrderId}", orderId);
        using var activity = AppTracing.StartActivity("ReceivingOrder.SetReceived", nameof(ReceivingOrderCommandService));

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var existingOrder = await dbContext.ReceivingOrders
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == orderId, ct);

        if (existingOrder is null)
        {
            logger.LogError("Not Found");
            return ServiceError.NotFound<ReceivingOrder>();
        }

        var validationResult = existingOrder.ValidateToSetReceived();

        if (!validationResult.IsSuccess)
        {
            logger.LogError("Validation to set received failed: {ErrorMessage}", validationResult.Error?.Message);
            return validationResult;
        }

        if (existingOrder.HasPlanFactDifference)
        {
            var externalItemsUpdateResult = await outboundService.UpdateDocumentItemsAsync(existingOrder.Id, existingOrder.Items, ct);

            if (!externalItemsUpdateResult.IsSuccess)
            {
                logger.LogError("Failed to update external order items: {ErrorMessage}", externalItemsUpdateResult.Error?.Message);
                return externalItemsUpdateResult;
            }
        }

        var externalResult = await outboundService.SetReceivedAsync(orderId, ct);

        if (!externalResult.IsSuccess)
        {
            logger.LogError("Failed to set external document received: {ErrorMessage}", externalResult.Error?.Message);
            return externalResult;
        }

        existingOrder.SetReceived(userId);

        var balanceAndTurnoverResult = await balanceAndTurnoverService
            .PostReceivedOrderInventoryAsync(existingOrder, dbContext, ct);

        if (!balanceAndTurnoverResult.IsSuccess)
            return balanceAndTurnoverResult;

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
            return ServiceError.Invalid<ReceivingOrderItem>("Fact quantity cannot be negative.");

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var existingOrder = await dbContext.ReceivingOrders
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == receivingOrderId, ct);

        if (existingOrder is null)
            return ServiceError.NotFound<ReceivingOrder>();

        var canEditFactQuantity = existingOrder.Status is ReceivingOrderStatus.InReceiving or ReceivingOrderStatus.ProcessingRequired;

        if (!canEditFactQuantity)
            return ServiceError.Invalid<ReceivingOrderItem>("Fact quantity can be edited only while the receiving order is in receiving or requires processing.");

        var existingItem = existingOrder.Items.FirstOrDefault(x => x.LineNumber == lineNumber);

        if (existingItem is null)
            return ServiceError.NotFound<ReceivingOrderItem>();

        existingItem.FactQuantity = factQuantity;
        existingItem.Comment = comment;

        await dbContext.SaveChangesAsync(ct);

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

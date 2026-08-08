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
                logger.LogDebug("External document status is shipped, new order create not allowed");

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
            // Чтобы разрешить для статуса Shipped, вероятно, потребуется доработка (откат BalanceAndTurnover...)
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

    public async Task<ServiceResult> StartPickingAsync(Guid orderId, string userId, CancellationToken ct = default)
    {
        using var scope = logger.BeginScope("ShippingOrder StartPicking {OrderId}", orderId);

        using var activity = AppTracing.StartActivity("ShippingOrder.StartPicking", nameof(ShippingOrderCommandService));

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var existingOrder = await dbContext.ShippingOrders
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == orderId, ct);

        if (existingOrder is null)
        {
            logger.LogError("Not Found");
            return ServiceError.NotFound<ShippingOrder>();
        }

        var validationResult = existingOrder.ValidateToStartPicking();

        if (!validationResult.IsSuccess)
        {
            logger.LogError("Validation to start picking failed: {ErrorMessage}", validationResult.Error?.Message);
            return validationResult;
        }

        var externalStartResult = await outboundService.StartPickingAsync(orderId, ct);

        if (!externalStartResult.IsSuccess)
        {
            logger.LogError("Failed to start picking in external document: {ErrorMessage}", externalStartResult.Error?.Message);
            return ServiceError.Failure<ShippingOrder>("Failed to start picking in external document");
        }

        existingOrder.StartPicking(userId);

        await dbContext.SaveChangesAsync(ct);

        return ServiceResult.Success();
    }

    public async Task<ServiceResult> MarkReadyForShipmentAsync(Guid orderId, string userId, CancellationToken ct = default)
    {
        using var scope = logger.BeginScope("ShippingOrder MarkReadyForShipment {OrderId}", orderId);

        using var activity = AppTracing.StartActivity("ShippingOrder.MarkReadyForShipment", nameof(ShippingOrderCommandService));

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var existingOrder = await dbContext.ShippingOrders
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == orderId, ct);

        if (existingOrder is null)
        {
            logger.LogError("Not Found");
            return ServiceError.NotFound<ShippingOrder>();
        }

        var validationResult = existingOrder.ValidateToMarkReadyForShipment();

        if (!validationResult.IsSuccess)
        {
            logger.LogError("Validation to mark ready for shipment failed: {ErrorMessage}", validationResult.Error?.Message);
            return validationResult;
        }

        var externalItemsUpdateResult = await outboundService
            .UpdateDocumentItemsAsync(existingOrder, ct);

        if (!externalItemsUpdateResult.IsSuccess)
        {
            logger.LogError("Failed to update external order items: {ErrorMessage}", externalItemsUpdateResult.Error?.Message);
            return externalItemsUpdateResult;
        }

        var externalReadyForShipmentResult = await outboundService.MarkReadyForShipmentAsync(orderId, ct);

        if (!externalReadyForShipmentResult.IsSuccess)
        {
            logger.LogError("Failed to mark external document ready for shipment: {ErrorMessage}", externalReadyForShipmentResult.Error?.Message);
            return ServiceError.Failure<ShippingOrder>("Failed to mark external document ready for shipment");
        }

        existingOrder.MarkReadyForShipment(userId);

        await dbContext.SaveChangesAsync(ct);

        return ServiceResult.Success();
    }

    public async Task<ServiceResult> ShipAsync(Guid orderId, string userId, CancellationToken ct = default)
    {
        using var scope = logger.BeginScope("ShippingOrder Ship {OrderId}", orderId);

        using var activity = AppTracing.StartActivity("ShippingOrder.Ship", nameof(ShippingOrderCommandService));

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var existingOrder = await dbContext.ShippingOrders
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == orderId, ct);

        if (existingOrder is null)
        {
            logger.LogError("Not Found");
            return ServiceError.NotFound<ShippingOrder>();
        }

        var validationResult = existingOrder.ValidateToShip();

        if (!validationResult.IsSuccess)
        {
            logger.LogError("Validation to ship failed: {ErrorMessage}", validationResult.Error?.Message);
            return validationResult;
        }

        var externalShipmentResult = await outboundService.ShipAsync(orderId, ct);

        if (!externalShipmentResult.IsSuccess)
        {
            logger.LogError("Failed to ship external document: {ErrorMessage}", externalShipmentResult.Error?.Message);
            return ServiceError.Failure<ShippingOrder>("Failed to ship external document");
        }

        existingOrder.Ship(userId);

        var balanceAndTurnoverResult = await balanceAndTurnoverService
            .ShipShippingOrder(existingOrder, dbContext, ct);

        if (!balanceAndTurnoverResult.IsSuccess)
            return balanceAndTurnoverResult;

        await dbContext.SaveChangesAsync(ct);

        return ServiceResult.Success();
    }
}

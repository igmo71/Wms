using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wms.Common;
using Wms.Data;
using Wms.Domain;
using Wms.Domain.Enums;
using Wms.Integration.OneS.Services;

namespace Wms.Application.Services;

internal class ShippingOrderCommandService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    BalanceAndTurnoverService balanceAndTurnoverService,
    Document_РасходныйОрдерНаТовары_OutboundService outboundService,
    ILogger<ShippingOrderCommandService> logger)
{
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
            if (externalOrder.Status != ShippingOrderStatus.Prepared)
            {
                logger.LogWarning("External shipping order create is not allowed for status {Status}", externalOrder.Status);
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

            if (existingOrder.Status != ShippingOrderStatus.Prepared)
            {
                existingOrder.ExternalChangeDetected = true;

                logger.LogWarning("External shipping order changes conflict. Local status: {LocalStatus}, external status: {ExternalStatus}",
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

    public async Task<ServiceResult> SetReadyForPickingAsync(Guid orderId, string userId, CancellationToken ct = default)
    {
        using var scope = logger.BeginScope("ShippingOrder SetReadyForPicking {OrderId}", orderId);
        using var activity = AppTracing.StartActivity("ShippingOrder.SetReadyForPicking", nameof(ShippingOrderCommandService));

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var existingOrder = await dbContext.ShippingOrders
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == orderId, ct);

        if (existingOrder is null)
        {
            logger.LogError("Not Found");
            return ServiceError.NotFound<ShippingOrder>();
        }

        var validationResult = existingOrder.ValidateToSetReadyForPicking();

        if (!validationResult.IsSuccess)
        {
            logger.LogError("Validation to set ready for picking failed: {ErrorMessage}", validationResult.Error?.Message);
            return validationResult;
        }

        var externalResult = await outboundService.SetReadyForPickingAsync(orderId, ct);

        if (!externalResult.IsSuccess)
        {
            logger.LogError("Failed to set external document ready for picking: {ErrorMessage}", externalResult.Error?.Message);
            return externalResult;
        }

        existingOrder.SetReadyForPicking(userId);

        await dbContext.SaveChangesAsync(ct);

        return ServiceResult.Success();
    }

    public async Task<ServiceResult> SetReadyForShipmentAsync(Guid orderId, string userId, CancellationToken ct = default)
    {
        using var scope = logger.BeginScope("ShippingOrder SetReadyForShipment {OrderId}", orderId);
        using var activity = AppTracing.StartActivity("ShippingOrder.SetReadyForShipment", nameof(ShippingOrderCommandService));

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var existingOrder = await dbContext.ShippingOrders
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == orderId, ct);

        if (existingOrder is null)
        {
            logger.LogError("Not Found");
            return ServiceError.NotFound<ShippingOrder>();
        }

        var validationResult = existingOrder.ValidateToSetReadyForShipment();

        if (!validationResult.IsSuccess)
        {
            logger.LogError("Validation to set ready for shipment failed: {ErrorMessage}", validationResult.Error?.Message);
            return validationResult;
        }

        var draftPickingMovements = await dbContext.InventoryMovements
            .Where(x => x.PostedAtUtc == null
                && x.RecorderType == RecorderType.ShippingOrder
                && x.RecorderId == existingOrder.Id)
            .ToListAsync(ct);

        var balanceAndTurnoverResult = await balanceAndTurnoverService
            .PostInventoryMovementsAsync(draftPickingMovements, dbContext, ct);

        if (!balanceAndTurnoverResult.IsSuccess)
            return balanceAndTurnoverResult;

        if (existingOrder.HasPlanFactDifference)
        {
            var externalItemsUpdateResult = await outboundService.UpdateDocumentItemsAsync(existingOrder, ct);

            if (!externalItemsUpdateResult.IsSuccess)
            {
                logger.LogError("Failed to update external order items: {ErrorMessage}", externalItemsUpdateResult.Error?.Message);
                return externalItemsUpdateResult;
            }
        }

        var externalResult = await outboundService.SetReadyForShipmentAsync(orderId, ct);

        if (!externalResult.IsSuccess)
        {
            logger.LogError("Failed to set external document ready for shipment: {ErrorMessage}", externalResult.Error?.Message);
            return externalResult;
        }

        existingOrder.SetReadyForShipment(userId);

        await dbContext.SaveChangesAsync(ct);

        return ServiceResult.Success();
    }

    public async Task<ServiceResult> SetShippedAsync(Guid orderId, string userId, CancellationToken ct = default)
    {
        using var scope = logger.BeginScope("ShippingOrder SetShipped {OrderId}", orderId);
        using var activity = AppTracing.StartActivity("ShippingOrder.SetShipped", nameof(ShippingOrderCommandService));

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var existingOrder = await dbContext.ShippingOrders
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == orderId, ct);

        if (existingOrder is null)
        {
            logger.LogError("Not Found");
            return ServiceError.NotFound<ShippingOrder>();
        }

        var validationResult = existingOrder.ValidateToSetShipped();

        if (!validationResult.IsSuccess)
        {
            logger.LogError("Validation to set shipped failed: {ErrorMessage}", validationResult.Error?.Message);
            return validationResult;
        }

        var now = DateTimeOffset.UtcNow;
        var movements = existingOrder.Items
            .Where(x => x.FactQuantity != 0)
            .Select(x => new InventoryMovement
            {
                WarehouseId = existingOrder.WarehouseId,
                SourceStorageLocationId = existingOrder.ShippingLocationId,
                StockKeepingUnitId = x.StockKeepingUnitId,
                Quantity = x.FactQuantity,
                CreatedAtUtc = now,
                RecorderId = existingOrder.Id,
                RecorderLineNumber = x.LineNumber,
                RecorderType = RecorderType.ShippingOrder
            })
            .ToList();

        dbContext.InventoryMovements.AddRange(movements);

        var balanceAndTurnoverResult = await balanceAndTurnoverService
            .PostInventoryMovementsAsync(movements, dbContext, ct);

        if (!balanceAndTurnoverResult.IsSuccess)
            return balanceAndTurnoverResult;

        var externalResult = await outboundService.SetShippedAsync(orderId, ct);

        if (!externalResult.IsSuccess)
        {
            logger.LogError("Failed to set external document shipped: {ErrorMessage}", externalResult.Error?.Message);
            return externalResult;
        }

        existingOrder.SetShipped(userId);

        await dbContext.SaveChangesAsync(ct);

        return ServiceResult.Success();
    }
}

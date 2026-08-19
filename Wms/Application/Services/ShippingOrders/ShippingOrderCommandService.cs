using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wms.Application.Services.Inventory;
using Wms.Common;
using Wms.Data;
using Wms.Domain;
using Wms.Domain.Enums;
using Wms.Integration.OneS.Services;

namespace Wms.Application.Services.ShippingOrders;

public class ShippingOrderCommandService(
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

    public async Task<OperationResult> SetReadyForPickingAsync(Guid orderId, string userId, CancellationToken ct = default)
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
            return OperationError.NotFound<ShippingOrder>();
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

        return OperationResult.Success();
    }

    public async Task<OperationResult> SetReadyForShipmentAsync(Guid orderId, string userId, CancellationToken ct = default)
    {
        using var scope = logger.BeginScope("ShippingOrder SetReadyForShipment {OrderId}", orderId);
        using var activity = AppTracing.StartActivity("ShippingOrder.SetReadyForShipment", nameof(ShippingOrderCommandService));

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var existingOrder = await dbContext.ShippingOrders
            .Include(x => x.Items)
            .Include(x => x.BaseItems)
            .FirstOrDefaultAsync(x => x.Id == orderId, ct);

        if (existingOrder is null)
        {
            logger.LogError("Not Found");
            return OperationError.NotFound<ShippingOrder>();
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

        var externalItemsUpdateResult = await outboundService.UpdateDocumentItemsAsync(existingOrder, ct);

        if (!externalItemsUpdateResult.IsSuccess)
        {
            logger.LogError("Failed to update external order items: {ErrorMessage}", externalItemsUpdateResult.Error?.Message);
            return externalItemsUpdateResult;
        }

        var externalResult = await outboundService.SetReadyForShipmentAsync(orderId, ct);

        if (!externalResult.IsSuccess)
        {
            logger.LogError("Failed to set external document ready for shipment: {ErrorMessage}", externalResult.Error?.Message);
            return externalResult;
        }

        existingOrder.SetReadyForShipment(userId);

        await dbContext.SaveChangesAsync(ct);

        return OperationResult.Success();
    }

    public async Task<OperationResult> SetShippedAsync(Guid orderId, string userId, CancellationToken ct = default)
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
            return OperationError.NotFound<ShippingOrder>();
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

        return OperationResult.Success();
    }

    public async Task<OperationResult> SetShippingLocationAsync(
        Guid shippingOrderId,
        Guid shippingLocationId,
        CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var orderWarehouseId = await dbContext.ShippingOrders
            .Where(x => x.Id == shippingOrderId)
            .Select(x => (Guid?)x.WarehouseId)
            .FirstOrDefaultAsync(ct);

        if (orderWarehouseId is null)
            return OperationError.NotFound<ShippingOrder>();

        var validLocation = await dbContext.StorageLocations
            .AnyAsync(x => x.Id == shippingLocationId
                && x.WarehouseId == orderWarehouseId
                && !x.IsFolder
                && !x.DeletionMark
                && !x.Zone!.DeletionMark
                && x.Zone!.Type == ZoneType.Shipping, ct);

        if (!validLocation)
            return OperationError.Invalid<StorageLocation>("Shipping location must belong to a shipping zone in the order warehouse.");

        var affected = await dbContext.ShippingOrders
            .Where(x => x.Id == shippingOrderId)
            .ExecuteUpdateAsync(x => x
                .SetProperty(p => p.ShippingLocationId, shippingLocationId), ct);

        if (affected == 0)
            return OperationError.NotFound<ShippingOrder>();

        return OperationResult.Success();
    }

    public async Task<OperationResult> RollbackAsync(
        Guid orderId,
        string reason,
        string userId,
        CancellationToken ct = default)
    {
        using var scope = logger.BeginScope("ShippingOrder Rollback {OrderId}", orderId);
        using var activity = AppTracing.StartActivity("ShippingOrder.Rollback", nameof(ShippingOrderCommandService));

        if (string.IsNullOrWhiteSpace(reason))
            return OperationError.Invalid<ShippingOrder>("Rollback reason must be specified.");

        if (string.IsNullOrWhiteSpace(userId))
            return OperationError.Invalid<ShippingOrder>("Rollback user must be specified.");

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var order = await dbContext.ShippingOrders
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == orderId, ct);

        if (order is null)
        {
            logger.LogError("Shipping order not found");
            return OperationError.NotFound<ShippingOrder>();
        }

        var validationResult = order.ValidateToRollback();
        if (!validationResult.IsSuccess)
        {
            logger.LogError("Shipping order rollback validation failed: {ErrorMessage}", validationResult.Error?.Message);
            return validationResult;
        }

        var draftMovements = await dbContext.InventoryMovements
            .Where(x => x.PostedAtUtc == null
                && x.RecorderType == RecorderType.ShippingOrder
                && x.RecorderId == order.Id)
            .ToListAsync(ct);

        var postedMovements = await dbContext.InventoryMovements
            .Where(x => x.PostedAtUtc != null
                && x.CreatedAtUtc >= order.PickingStartedAtUtc!.Value
                && x.RecorderType == RecorderType.ShippingOrder
                && x.RecorderId == order.Id)
            .ToListAsync(ct);

        if (postedMovements.Any(x => x.SourceStorageLocationId is null
            || x.DestinationStorageLocationId != order.ShippingLocationId))
        {
            logger.LogError("Shipping order rollback found an unexpected posted movement");
            return OperationError.Failure<ShippingOrder>(
                "Shipping order contains movements that cannot be rolled back safely.");
        }

        var now = DateTimeOffset.UtcNow;
        var compensationMovements = postedMovements
            .OrderByDescending(x => x.PostedAtUtc)
            .Select(x => new InventoryMovement
            {
                WarehouseId = x.WarehouseId,
                SourceStorageLocationId = x.DestinationStorageLocationId,
                DestinationStorageLocationId = x.SourceStorageLocationId,
                StockKeepingUnitId = x.StockKeepingUnitId,
                Quantity = x.Quantity,
                CreatedAtUtc = now,
                RecorderId = order.Id,
                RecorderLineNumber = x.RecorderLineNumber,
                RecorderType = RecorderType.ShippingOrder
            })
            .ToList();

        dbContext.InventoryMovements.RemoveRange(draftMovements);

        if (compensationMovements.Count > 0)
        {
            dbContext.InventoryMovements.AddRange(compensationMovements);

            var postingResult = await balanceAndTurnoverService
                .PostInventoryMovementsAsync(compensationMovements, dbContext, ct);

            if (!postingResult.IsSuccess)
            {
                logger.LogError("Shipping order rollback failed to compensate movements: {ErrorMessage}", postingResult.Error?.Message);
                return postingResult;
            }
        }

        order.Rollback(reason.Trim(), userId);

        await dbContext.SaveChangesAsync(ct);

        logger.LogInformation("Shipping order rolled back by {UserId}. Reason: {Reason}", userId, reason.Trim());
        return OperationResult.Success();
    }
}

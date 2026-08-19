using System.Diagnostics;
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
    public async Task<OperationResult> ImportOrderAsync(
        ShippingOrderImportSnapshot snapshot,
        CancellationToken ct = default)
    {
        using IDisposable? scope = logger.BeginScope("ShippingOrder Import {OrderId}", snapshot.Id);
        using Activity? activity = AppTracing.StartActivity("ShippingOrder.Import", nameof(ShippingOrderCommandService));

        await using ApplicationDbContext dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        ShippingOrder? existingOrder = await dbContext.ShippingOrders
            .Include(x => x.Items)
            .Include(x => x.BaseItems)
            .FirstOrDefaultAsync(x => x.Id == snapshot.Id, ct);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (existingOrder is null)
        {
            OperationResult<ShippingOrder> creationResult = ShippingOrder.Create(snapshot, now);
            if (!creationResult.IsSuccess)
            {
                return creationResult.Error!;
            }

            dbContext.ShippingOrders.Add(creationResult.Value!);
            await dbContext.SaveChangesAsync(ct);
            return OperationResult.Success();
        }

        OperationResult<ShippingOrderReconciliation> reconciliationResult = existingOrder.Reconcile(snapshot, now);
        if (!reconciliationResult.IsSuccess)
        {
            return reconciliationResult.Error!;
        }

        if (reconciliationResult.Value == ShippingOrderReconciliation.Unchanged)
        {
            logger.LogDebug("No external document changes detected");
            return OperationResult.Success();
        }

        await dbContext.SaveChangesAsync(ct);
        if (reconciliationResult.Value == ShippingOrderReconciliation.Conflict)
        {
            logger.LogWarning(
                "External shipping order changes conflict. Local status: {LocalStatus}, external status: {ExternalStatus}",
                existingOrder.Status,
                snapshot.Status);
            return OperationError.Conflict(
                "External shipping order changes conflict with local processing.");
        }

        return OperationResult.Success();
    }

    public async Task<OperationResult> SetReadyForPickingAsync(Guid orderId, string userId, CancellationToken ct = default)
    {
        using IDisposable? scope = logger.BeginScope("ShippingOrder SetReadyForPicking {OrderId}", orderId);
        using Activity? activity = AppTracing.StartActivity("ShippingOrder.SetReadyForPicking", nameof(ShippingOrderCommandService));

        await using ApplicationDbContext dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        ShippingOrder? existingOrder = await dbContext.ShippingOrders
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == orderId, ct);

        if (existingOrder is null)
        {
            logger.LogError("Not Found");
            return OperationError.NotFound<ShippingOrder>();
        }

        OperationResult transitionResult = existingOrder.SetReadyForPicking(DateTimeOffset.UtcNow, userId);
        if (!transitionResult.IsSuccess)
        {
            logger.LogError("Validation to set ready for picking failed: {ErrorMessage}", transitionResult.Error?.Message);
            return transitionResult;
        }

        OperationResult externalResult = await outboundService.SetReadyForPickingAsync(orderId, ct);

        if (!externalResult.IsSuccess)
        {
            logger.LogError("Failed to set external document ready for picking: {ErrorMessage}", externalResult.Error?.Message);
            return externalResult;
        }

        await dbContext.SaveChangesAsync(ct);

        return OperationResult.Success();
    }

    public async Task<OperationResult> SetReadyForShipmentAsync(Guid orderId, string userId, CancellationToken ct = default)
    {
        using IDisposable? scope = logger.BeginScope("ShippingOrder SetReadyForShipment {OrderId}", orderId);
        using Activity? activity = AppTracing.StartActivity("ShippingOrder.SetReadyForShipment", nameof(ShippingOrderCommandService));

        await using ApplicationDbContext dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        ShippingOrder? existingOrder = await dbContext.ShippingOrders
            .Include(x => x.Items)
            .Include(x => x.BaseItems)
            .FirstOrDefaultAsync(x => x.Id == orderId, ct);

        if (existingOrder is null)
        {
            logger.LogError("Not Found");
            return OperationError.NotFound<ShippingOrder>();
        }

        List<InventoryMovement> draftPickingMovements = await dbContext.InventoryMovements
            .Where(x => x.PostedAtUtc == null
                && x.RecorderType == RecorderType.ShippingOrder
                && x.RecorderId == existingOrder.Id)
            .ToListAsync(ct);

        OperationResult transitionResult = existingOrder.SetReadyForShipment(
            draftPickingMovements,
            DateTimeOffset.UtcNow,
            userId);
        if (!transitionResult.IsSuccess)
        {
            logger.LogError("Validation to set ready for shipment failed: {ErrorMessage}", transitionResult.Error?.Message);
            return transitionResult;
        }

        OperationResult balanceAndTurnoverResult = await balanceAndTurnoverService
            .PostInventoryMovementsAsync(draftPickingMovements, dbContext, ct);

        if (!balanceAndTurnoverResult.IsSuccess)
        {
            return balanceAndTurnoverResult;
        }

        OperationResult externalItemsUpdateResult = await outboundService.UpdateDocumentItemsAsync(existingOrder, ct);

        if (!externalItemsUpdateResult.IsSuccess)
        {
            logger.LogError("Failed to update external order items: {ErrorMessage}", externalItemsUpdateResult.Error?.Message);
            return externalItemsUpdateResult;
        }

        OperationResult externalResult = await outboundService.SetReadyForShipmentAsync(orderId, ct);

        if (!externalResult.IsSuccess)
        {
            logger.LogError("Failed to set external document ready for shipment: {ErrorMessage}", externalResult.Error?.Message);
            return externalResult;
        }

        await dbContext.SaveChangesAsync(ct);

        return OperationResult.Success();
    }

    public async Task<OperationResult> SetShippedAsync(Guid orderId, string userId, CancellationToken ct = default)
    {
        using IDisposable? scope = logger.BeginScope("ShippingOrder SetShipped {OrderId}", orderId);
        using Activity? activity = AppTracing.StartActivity("ShippingOrder.SetShipped", nameof(ShippingOrderCommandService));

        await using ApplicationDbContext dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        ShippingOrder? existingOrder = await dbContext.ShippingOrders
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == orderId, ct);

        if (existingOrder is null)
        {
            logger.LogError("Not Found");
            return OperationError.NotFound<ShippingOrder>();
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        OperationResult transitionResult = existingOrder.SetShipped(now, userId);
        if (!transitionResult.IsSuccess)
        {
            logger.LogError("Validation to set shipped failed: {ErrorMessage}", transitionResult.Error?.Message);
            return transitionResult;
        }

        OperationResult<List<InventoryMovement>> movementsResult = CreateShippingMovements(existingOrder, now);
        if (!movementsResult.IsSuccess)
        {
            return movementsResult.Error!;
        }

        List<InventoryMovement> movements = movementsResult.Value!;
        dbContext.InventoryMovements.AddRange(movements);

        OperationResult balanceAndTurnoverResult = await balanceAndTurnoverService
            .PostInventoryMovementsAsync(movements, dbContext, ct);

        if (!balanceAndTurnoverResult.IsSuccess)
        {
            return balanceAndTurnoverResult;
        }

        OperationResult externalResult = await outboundService.SetShippedAsync(orderId, ct);

        if (!externalResult.IsSuccess)
        {
            logger.LogError("Failed to set external document shipped: {ErrorMessage}", externalResult.Error?.Message);
            return externalResult;
        }

        await dbContext.SaveChangesAsync(ct);

        return OperationResult.Success();
    }

    public async Task<OperationResult> SetShippingLocationAsync(
        Guid shippingOrderId,
        Guid shippingLocationId,
        CancellationToken ct = default)
    {
        await using ApplicationDbContext dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        ShippingOrder? order = await dbContext.ShippingOrders
            .FirstOrDefaultAsync(x => x.Id == shippingOrderId, ct);

        if (order is null)
        {
            return OperationError.NotFound<ShippingOrder>();
        }

        bool validLocation = await dbContext.StorageLocations
            .AnyAsync(x => x.Id == shippingLocationId
                && x.WarehouseId == order.WarehouseId
                && !x.IsFolder
                && !x.DeletionMark
                && !x.Zone!.DeletionMark
                && x.Zone!.Type == ZoneType.Shipping, ct);

        if (!validLocation)
        {
            return OperationError.Invalid<StorageLocation>("Shipping location must belong to a shipping zone in the order warehouse.");
        }

        OperationResult locationResult = order.SetShippingLocation(shippingLocationId);
        if (!locationResult.IsSuccess)
        {
            return locationResult;
        }

        await dbContext.SaveChangesAsync(ct);
        return OperationResult.Success();
    }

    public async Task<OperationResult> RollbackAsync(
        Guid orderId,
        string reason,
        string userId,
        CancellationToken ct = default)
    {
        using IDisposable? scope = logger.BeginScope("ShippingOrder Rollback {OrderId}", orderId);
        using Activity? activity = AppTracing.StartActivity("ShippingOrder.Rollback", nameof(ShippingOrderCommandService));

        await using ApplicationDbContext dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        ShippingOrder? order = await dbContext.ShippingOrders
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == orderId, ct);

        if (order is null)
        {
            logger.LogError("Shipping order not found");
            return OperationError.NotFound<ShippingOrder>();
        }

        List<InventoryMovement> draftMovements = await dbContext.InventoryMovements
            .Where(x => x.PostedAtUtc == null
                && x.RecorderType == RecorderType.ShippingOrder
                && x.RecorderId == order.Id)
            .ToListAsync(ct);

        List<InventoryMovement> postedMovements = await dbContext.InventoryMovements
            .Where(x => x.PostedAtUtc != null
                && x.RecorderType == RecorderType.ShippingOrder
                && x.RecorderId == order.Id)
            .ToListAsync(ct);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        OperationResult<List<InventoryMovement>> rollbackResult = order.Rollback(
            reason,
            userId,
            now,
            draftMovements,
            postedMovements);
        if (!rollbackResult.IsSuccess)
        {
            logger.LogError("Shipping order rollback validation failed: {ErrorMessage}", rollbackResult.Error?.Message);
            return rollbackResult.Error!;
        }

        List<InventoryMovement> compensationMovements = rollbackResult.Value!;
        dbContext.InventoryMovements.RemoveRange(draftMovements);

        if (compensationMovements.Count > 0)
        {
            dbContext.InventoryMovements.AddRange(compensationMovements);

            OperationResult postingResult = await balanceAndTurnoverService
                .PostInventoryMovementsAsync(compensationMovements, dbContext, ct);

            if (!postingResult.IsSuccess)
            {
                logger.LogError("Shipping order rollback failed to compensate movements: {ErrorMessage}", postingResult.Error?.Message);
                return postingResult;
            }
        }

        await dbContext.SaveChangesAsync(ct);

        logger.LogInformation("Shipping order rolled back by {UserId}. Reason: {Reason}", userId, reason.Trim());
        return OperationResult.Success();
    }

    private static OperationResult<List<InventoryMovement>> CreateShippingMovements(
        ShippingOrder order,
        DateTimeOffset createdAtUtc)
    {
        var movements = new List<InventoryMovement>();
        foreach (ShippingOrderItem? item in order.Items.Where(x => x.FactQuantity != 0))
        {
            OperationResult<InventoryMovement> movementResult = InventoryMovement.Create(
                Guid.NewGuid(),
                order.WarehouseId,
                order.ShippingLocationId,
                null,
                item.StockKeepingUnitId,
                item.FactQuantity,
                createdAtUtc,
                RecorderType.ShippingOrder,
                order.Id,
                item.LineNumber);
            if (!movementResult.IsSuccess)
            {
                return movementResult.Error!;
            }

            movements.Add(movementResult.Value!);
        }

        return movements;
    }

}

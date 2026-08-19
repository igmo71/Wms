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
        using var scope = logger.BeginScope("ShippingOrder Import {OrderId}", snapshot.Id);
        using var activity = AppTracing.StartActivity("ShippingOrder.Import", nameof(ShippingOrderCommandService));

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var existingOrder = await dbContext.ShippingOrders
            .Include(x => x.Items)
            .Include(x => x.BaseItems)
            .FirstOrDefaultAsync(x => x.Id == snapshot.Id, ct);

        var now = DateTimeOffset.UtcNow;
        if (existingOrder is null)
        {
            var creationResult = ShippingOrder.Create(snapshot, now);
            if (!creationResult.IsSuccess)
            {
                return creationResult.Error!;
            }

            dbContext.ShippingOrders.Add(creationResult.Value!);
            await dbContext.SaveChangesAsync(ct);
            return OperationResult.Success();
        }

        var reconciliationResult = existingOrder.Reconcile(snapshot, now);
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

        var transitionResult = existingOrder.SetReadyForPicking(DateTimeOffset.UtcNow, userId);
        if (!transitionResult.IsSuccess)
        {
            logger.LogError("Validation to set ready for picking failed: {ErrorMessage}", transitionResult.Error?.Message);
            return transitionResult;
        }

        var externalResult = await outboundService.SetReadyForPickingAsync(orderId, ct);

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

        var transitionResult = existingOrder.SetReadyForShipment(DateTimeOffset.UtcNow, userId);
        if (!transitionResult.IsSuccess)
        {
            logger.LogError("Validation to set ready for shipment failed: {ErrorMessage}", transitionResult.Error?.Message);
            return transitionResult;
        }

        var draftPickingMovements = await dbContext.InventoryMovements
            .Where(x => x.PostedAtUtc == null
                && x.RecorderType == RecorderType.ShippingOrder
                && x.RecorderId == existingOrder.Id)
            .ToListAsync(ct);

        var balanceAndTurnoverResult = await balanceAndTurnoverService
            .PostInventoryMovementsAsync(draftPickingMovements, dbContext, ct);

        if (!balanceAndTurnoverResult.IsSuccess)
        {
            return balanceAndTurnoverResult;
        }

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

        var now = DateTimeOffset.UtcNow;
        var transitionResult = existingOrder.SetShipped(now, userId);
        if (!transitionResult.IsSuccess)
        {
            logger.LogError("Validation to set shipped failed: {ErrorMessage}", transitionResult.Error?.Message);
            return transitionResult;
        }

        var movementsResult = CreateShippingMovements(existingOrder, now);
        if (!movementsResult.IsSuccess)
        {
            return movementsResult.Error!;
        }

        var movements = movementsResult.Value!;
        dbContext.InventoryMovements.AddRange(movements);

        var balanceAndTurnoverResult = await balanceAndTurnoverService
            .PostInventoryMovementsAsync(movements, dbContext, ct);

        if (!balanceAndTurnoverResult.IsSuccess)
        {
            return balanceAndTurnoverResult;
        }

        var externalResult = await outboundService.SetShippedAsync(orderId, ct);

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
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var order = await dbContext.ShippingOrders
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

        var locationResult = order.SetShippingLocation(shippingLocationId);
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
        using var scope = logger.BeginScope("ShippingOrder Rollback {OrderId}", orderId);
        using var activity = AppTracing.StartActivity("ShippingOrder.Rollback", nameof(ShippingOrderCommandService));

        if (string.IsNullOrWhiteSpace(reason))
        {
            return OperationError.Invalid<ShippingOrder>("Rollback reason must be specified.");
        }

        if (string.IsNullOrWhiteSpace(userId))
        {
            return OperationError.Invalid<ShippingOrder>("Rollback user must be specified.");
        }

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
        var compensationResult = CreateCompensationMovements(order, postedMovements, now);
        if (!compensationResult.IsSuccess)
        {
            return compensationResult.Error!;
        }

        var compensationMovements = compensationResult.Value!;
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

    private static OperationResult<List<InventoryMovement>> CreateShippingMovements(
        ShippingOrder order,
        DateTimeOffset createdAtUtc)
    {
        var movements = new List<InventoryMovement>();
        foreach (var item in order.Items.Where(x => x.FactQuantity != 0))
        {
            var movementResult = InventoryMovement.Create(
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

    private static OperationResult<List<InventoryMovement>> CreateCompensationMovements(
        ShippingOrder order,
        IEnumerable<InventoryMovement> postedMovements,
        DateTimeOffset createdAtUtc)
    {
        var movements = new List<InventoryMovement>();
        foreach (var postedMovement in postedMovements.OrderByDescending(x => x.PostedAtUtc))
        {
            var movementResult = InventoryMovement.Create(
                Guid.NewGuid(),
                postedMovement.WarehouseId,
                postedMovement.DestinationStorageLocationId,
                postedMovement.SourceStorageLocationId,
                postedMovement.StockKeepingUnitId,
                postedMovement.Quantity,
                createdAtUtc,
                RecorderType.ShippingOrder,
                order.Id,
                postedMovement.RecorderLineNumber);
            if (!movementResult.IsSuccess)
            {
                return movementResult.Error!;
            }

            movements.Add(movementResult.Value!);
        }

        return movements;
    }
}

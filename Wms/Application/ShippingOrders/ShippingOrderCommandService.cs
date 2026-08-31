using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wms.Application.Inventory.Movements;
using Wms.Application.Persistence;
using Wms.Application.StorageLocations;
using Wms.Common;
using Wms.Data;
using Wms.Domain;
using Wms.Domain.Enums;
using Wms.Integration.OneS.Services;

namespace Wms.Application.ShippingOrders;

public class ShippingOrderCommandService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    InventoryPostingService inventoryPostingService,
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
            logger.LogDebug("Изменения документа в 1С не обнаружены");
            return OperationResult.Success();
        }

        await dbContext.SaveChangesAsync(ct);
        if (reconciliationResult.Value == ShippingOrderReconciliation.Conflict)
        {
            logger.LogWarning("Изменения расходного ордера в 1С конфликтуют с локальными. Локальный статус: {LocalStatus}, статус 1С: {ExternalStatus}",
                existingOrder.Status, snapshot.Status);
            return OperationError.Conflict(
                "Изменения расходного ордера в 1С конфликтуют с локальной обработкой.");
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
            logger.LogError("Расходный ордер {OrderId} не найден", orderId);
            return OperationError.NotFound($"Расходный ордер '{orderId}' не найден.");
        }

        OperationResult transitionResult = existingOrder.SetReadyForPicking(DateTimeOffset.UtcNow, userId);
        if (!transitionResult.IsSuccess)
        {
            logger.LogError("Не удалось подготовить расходный ордер к отбору: {ErrorMessage}", transitionResult.Error?.Message);
            return transitionResult;
        }

        OperationResult externalResult = await outboundService.SetReadyForPickingAsync(orderId, ct);

        if (!externalResult.IsSuccess)
        {
            logger.LogError("Не удалось подготовить документ 1С к отбору: {ErrorMessage}", externalResult.Error?.Message);
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
            logger.LogError("Расходный ордер {OrderId} не найден", orderId);
            return OperationError.NotFound($"Расходный ордер '{orderId}' не найден.");
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
            logger.LogError("Не удалось подготовить расходный ордер к отгрузке: {ErrorMessage}", transitionResult.Error?.Message);
            return transitionResult;
        }

        OperationResult balanceAndTurnoverResult = await inventoryPostingService
            .PostInventoryMovementsAsync(draftPickingMovements, dbContext, ct);

        if (!balanceAndTurnoverResult.IsSuccess)
        {
            return balanceAndTurnoverResult;
        }

        OperationResult externalItemsUpdateResult = await outboundService.UpdateDocumentItemsAsync(existingOrder, ct);

        if (!externalItemsUpdateResult.IsSuccess)
        {
            logger.LogError("Не удалось обновить строки расходного ордера в 1С: {ErrorMessage}", externalItemsUpdateResult.Error?.Message);
            return externalItemsUpdateResult;
        }

        OperationResult externalResult = await outboundService.SetReadyForShipmentAsync(orderId, ct);

        if (!externalResult.IsSuccess)
        {
            logger.LogError("Не удалось подготовить документ 1С к отгрузке: {ErrorMessage}", externalResult.Error?.Message);
            return externalResult;
        }

        return await ApplicationPersistence.SaveChangesAsync(dbContext, ct);
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
            logger.LogError("Расходный ордер {OrderId} не найден", orderId);
            return OperationError.NotFound($"Расходный ордер '{orderId}' не найден.");
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        OperationResult transitionResult = existingOrder.SetShipped(now, userId);
        if (!transitionResult.IsSuccess)
        {
            logger.LogError("Не удалось завершить отгрузку расходного ордера: {ErrorMessage}", transitionResult.Error?.Message);
            return transitionResult;
        }

        OperationResult<List<InventoryMovement>> movementsResult = CreateShippingMovements(existingOrder, now);
        if (!movementsResult.IsSuccess)
        {
            return movementsResult.Error!;
        }

        List<InventoryMovement> movements = movementsResult.Value!;
        dbContext.InventoryMovements.AddRange(movements);

        OperationResult balanceAndTurnoverResult = await inventoryPostingService
            .PostInventoryMovementsAsync(movements, dbContext, ct);

        if (!balanceAndTurnoverResult.IsSuccess)
        {
            return balanceAndTurnoverResult;
        }

        OperationResult externalResult = await outboundService.SetShippedAsync(orderId, ct);

        if (!externalResult.IsSuccess)
        {
            logger.LogError("Не удалось завершить отгрузку документа в 1С: {ErrorMessage}", externalResult.Error?.Message);
            return externalResult;
        }

        return await ApplicationPersistence.SaveChangesAsync(dbContext, ct);
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
            return OperationError.NotFound($"Расходный ордер '{shippingOrderId}' не найден.");
        }

        var location = await dbContext.StorageLocations
            .Include(x => x.Zone)
            .Include(x => x.ActiveLock)
            .SingleOrDefaultAsync(x => x.Id == shippingLocationId, ct);

        if (location is null
            || location.WarehouseId != order.WarehouseId
            || location.IsFolder
            || location.DeletionMark
            || location.Zone?.DeletionMark == true
            || location.Zone?.Type != ZoneType.Shipping)
        {
            return OperationError.Invalid("Позиция отгрузки должна принадлежать зоне отгрузки на складе ордера.");
        }

        var availabilityResult = StorageLocationAvailability.ValidateUnlocked(location);
        if (!availabilityResult.IsSuccess)
        {
            return availabilityResult;
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
            logger.LogError("Расходный ордер {OrderId} не найден", orderId);
            return OperationError.NotFound($"Расходный ордер '{orderId}' не найден.");
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
            logger.LogError("Не удалось отменить операцию расходного ордера: {ErrorMessage}", rollbackResult.Error?.Message);
            return rollbackResult.Error!;
        }

        List<InventoryMovement> compensationMovements = rollbackResult.Value!;
        dbContext.InventoryMovements.RemoveRange(draftMovements);

        if (compensationMovements.Count > 0)
        {
            dbContext.InventoryMovements.AddRange(compensationMovements);

            OperationResult postingResult = await inventoryPostingService
                .PostInventoryMovementsAsync(compensationMovements, dbContext, ct);

            if (!postingResult.IsSuccess)
            {
                logger.LogError("При отмене операции расходного ордера не удалось компенсировать движения: {ErrorMessage}", postingResult.Error?.Message);
                return postingResult;
            }
        }

        var saveResult = await ApplicationPersistence.SaveChangesAsync(dbContext, ct);
        if (!saveResult.IsSuccess)
        {
            return saveResult;
        }

        logger.LogInformation("Операция расходного ордера отменена пользователем {UserId}. Причина: {Reason}", userId, reason.Trim());
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

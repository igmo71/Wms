using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wms.Application.Inventory.Movements;
using Wms.Common;
using Wms.Data;
using Wms.Domain;
using Wms.Domain.Enums;
using Wms.Integration.OneS.Services;

namespace Wms.Application.ReceivingOrders;

public class ReceivingOrderCommandService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    InventoryPostingService inventoryPostingService,
    Document_ПриходныйОрдерНаТовары_OutboundService outboundService,
    ILogger<ReceivingOrderCommandService> logger)
{
    public async Task<OperationResult> ImportOrderAsync(
        ReceivingOrderImportSnapshot snapshot,
        CancellationToken ct = default)
    {
        using var scope = logger.BeginScope("ReceivingOrder Import {OrderId}", snapshot.Id);
        using var activity = AppTracing.StartActivity("ReceivingOrder.Import", nameof(ReceivingOrderCommandService));

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var existingOrder = await dbContext.ReceivingOrders
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == snapshot.Id, ct);

        var now = DateTimeOffset.UtcNow;
        if (existingOrder is null)
        {
            var creationResult = ReceivingOrder.Create(snapshot, now);
            if (!creationResult.IsSuccess)
            {
                return creationResult.Error!;
            }

            dbContext.ReceivingOrders.Add(creationResult.Value!);
            await dbContext.SaveChangesAsync(ct);
            return OperationResult.Success();
        }

        var reconciliationResult = existingOrder.Reconcile(snapshot, now);
        if (!reconciliationResult.IsSuccess)
        {
            return reconciliationResult.Error!;
        }

        if (reconciliationResult.Value == ReceivingOrderReconciliation.Unchanged)
        {
            logger.LogDebug("Изменения документа в 1С не обнаружены");
            return OperationResult.Success();
        }

        await dbContext.SaveChangesAsync(ct);
        if (reconciliationResult.Value == ReceivingOrderReconciliation.Conflict)
        {
            logger.LogWarning("Изменения приходного ордера в 1С конфликтуют с локальными. Локальный статус: {LocalStatus}, статус 1С: {ExternalStatus}",
                existingOrder.Status, snapshot.Status);
            return OperationError.Conflict(
                "Изменения приходного ордера в 1С конфликтуют с локальной обработкой.");
        }

        return OperationResult.Success();
    }

    public async Task<OperationResult> SetInReceivingAsync(Guid orderId, string userId, CancellationToken ct = default)
    {
        using var scope = logger.BeginScope("ReceivingOrder SetInReceiving {OrderId}", orderId);
        using var activity = AppTracing.StartActivity("ReceivingOrder.SetInReceiving", nameof(ReceivingOrderCommandService));

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var existingOrder = await dbContext.ReceivingOrders
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == orderId, ct);

        if (existingOrder is null)
        {
            logger.LogError("Приходный ордер {OrderId} не найден", orderId);
            return OperationError.NotFound($"Приходный ордер '{orderId}' не найден.");
        }

        var transitionResult = existingOrder.SetInReceiving(DateTimeOffset.UtcNow, userId);
        if (!transitionResult.IsSuccess)
        {
            logger.LogError("Не удалось перевести приходный ордер в приемку: {ErrorMessage}", transitionResult.Error?.Message);
            return transitionResult;
        }

        var externalResult = await outboundService.SetInReceivingAsync(orderId, ct);

        if (!externalResult.IsSuccess)
        {
            logger.LogError("Не удалось перевести документ 1С в приемку: {ErrorMessage}", externalResult.Error?.Message);
            return externalResult;
        }

        await dbContext.SaveChangesAsync(ct);

        return OperationResult.Success();
    }

    public async Task<OperationResult> SetReceivedAsync(Guid orderId, string userId, CancellationToken ct = default)
    {
        using var scope = logger.BeginScope("ReceivingOrder SetReceived {OrderId}", orderId);
        using var activity = AppTracing.StartActivity("ReceivingOrder.SetReceived", nameof(ReceivingOrderCommandService));

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var existingOrder = await dbContext.ReceivingOrders
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == orderId, ct);

        if (existingOrder is null)
        {
            logger.LogError("Приходный ордер {OrderId} не найден", orderId);
            return OperationError.NotFound($"Приходный ордер '{orderId}' не найден.");
        }

        var now = DateTimeOffset.UtcNow;
        var transitionResult = existingOrder.SetReceived(now, userId);
        if (!transitionResult.IsSuccess)
        {
            logger.LogError("Не удалось завершить приемку приходного ордера: {ErrorMessage}", transitionResult.Error?.Message);
            return transitionResult;
        }

        var movementsResult = CreateReceivingMovements(existingOrder, now);
        if (!movementsResult.IsSuccess)
        {
            return movementsResult.Error!;
        }

        var movements = movementsResult.Value!;
        dbContext.InventoryMovements.AddRange(movements);

        var balanceAndTurnoverResult = await inventoryPostingService
            .PostInventoryMovementsAsync(movements, dbContext, ct);

        if (!balanceAndTurnoverResult.IsSuccess)
            return balanceAndTurnoverResult;

        if (existingOrder.HasPlanFactDifference)
        {
            var externalItemsUpdateResult = await outboundService.UpdateDocumentItemsAsync(existingOrder.Id, existingOrder.Items, ct);

            if (!externalItemsUpdateResult.IsSuccess)
            {
                logger.LogError("Не удалось обновить строки приходного ордера в 1С: {ErrorMessage}", externalItemsUpdateResult.Error?.Message);
                return externalItemsUpdateResult;
            }
        }

        var externalResult = await outboundService.SetReceivedAsync(orderId, ct);

        if (!externalResult.IsSuccess)
        {
            logger.LogError("Не удалось завершить приемку документа в 1С: {ErrorMessage}", externalResult.Error?.Message);
            return externalResult;
        }

        await dbContext.SaveChangesAsync(ct);

        return OperationResult.Success();
    }

    public async Task<OperationResult> UpdateOrderItemFactQuantityAsync(
        Guid receivingOrderId,
        int lineNumber,
        double factQuantity,
        string? comment,
        CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var existingOrder = await dbContext.ReceivingOrders
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == receivingOrderId, ct);

        if (existingOrder is null)
        {
            return OperationError.NotFound($"Приходный ордер '{receivingOrderId}' не найден.");
        }

        var updateResult = existingOrder.UpdateItemFact(lineNumber, factQuantity, comment);
        if (!updateResult.IsSuccess)
        {
            return updateResult;
        }

        await dbContext.SaveChangesAsync(ct);

        return OperationResult.Success();
    }

    public async Task<OperationResult> SetReceivingLocationAsync(
        Guid receivingOrderId,
        Guid receivingLocationId,
        CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var order = await dbContext.ReceivingOrders
            .FirstOrDefaultAsync(x => x.Id == receivingOrderId, ct);
        if (order is null)
        {
            return OperationError.NotFound($"Приходный ордер '{receivingOrderId}' не найден.");
        }

        var validLocation = await dbContext.StorageLocations
            .AnyAsync(x => x.Id == receivingLocationId
                && x.WarehouseId == order.WarehouseId
                && !x.IsFolder
                && !x.DeletionMark
                && !x.Zone!.DeletionMark
                && x.Zone!.Type == ZoneType.Receiving, ct);

        if (!validLocation)
        {
            return OperationError.Invalid("Позиция приёмки должна принадлежать зоне приёмки на складе ордера.");
        }

        var locationResult = order.SetReceivingLocation(receivingLocationId);
        if (!locationResult.IsSuccess)
        {
            return locationResult;
        }

        await dbContext.SaveChangesAsync(ct);
        return OperationResult.Success();
    }

    private static OperationResult<List<InventoryMovement>> CreateReceivingMovements(
        ReceivingOrder order,
        DateTimeOffset createdAtUtc)
    {
        var movements = new List<InventoryMovement>();
        foreach (var item in order.Items.Where(x => x.FactQuantity != 0))
        {
            var movementResult = InventoryMovement.Create(
                Guid.NewGuid(),
                order.WarehouseId,
                null,
                order.ReceivingLocationId,
                item.StockKeepingUnitId,
                item.FactQuantity,
                createdAtUtc,
                RecorderType.ReceivingOrder,
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

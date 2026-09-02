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

namespace Wms.Application.ReceivingOrders;

public class ReceivingOrderCommandService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    InventoryPostingService inventoryPostingService,
    IReceivingOrderSource orderSource,
    Document_ПриходныйОрдерНаТовары_OutboundService outboundService,
    ILogger<ReceivingOrderCommandService> logger)
{
    internal async Task<OperationResult<OrderSynchronizationAssessment>> SynchronizeOrderAsync(
        ReceivingOrderImportSnapshot snapshot,
        bool allowCreate,
        CancellationToken ct = default)
    {
        using var scope = logger.BeginScope("ReceivingOrder Synchronize {OrderId}", snapshot.Id);
        using var activity = AppTracing.StartActivity("ReceivingOrder.Synchronize", nameof(ReceivingOrderCommandService));

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var existingOrder = await dbContext.ReceivingOrders
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == snapshot.Id, ct);

        var now = DateTimeOffset.UtcNow;
        if (existingOrder is null)
        {
            if (!allowCreate)
            {
                return OperationError.NotFound(
                    $"Приходный ордер '{snapshot.Id}' не найден в WMS.");
            }

            var creationResult = ReceivingOrder.Create(snapshot, now);
            if (!creationResult.IsSuccess)
            {
                return creationResult.Error!;
            }

            ReceivingOrder createdOrder = creationResult.Value!;
            dbContext.ReceivingOrders.Add(createdOrder);
            OperationResult saveCreationResult = await ApplicationPersistence.SaveChangesAsync(dbContext, ct);
            return saveCreationResult.IsSuccess
                ? ReceivingOrderSynchronizationComparer.Compare(createdOrder, snapshot)
                : saveCreationResult.Error!;
        }

        OrderSynchronizationAssessment assessment =
            ReceivingOrderSynchronizationComparer.Compare(existingOrder, snapshot);
        var reconciliationResult = existingOrder.Reconcile(snapshot, now);
        if (!reconciliationResult.IsSuccess)
        {
            return reconciliationResult.Error!;
        }

        if (reconciliationResult.Value == ReceivingOrderReconciliation.Unchanged)
        {
            logger.LogDebug("Изменения документа в 1С не обнаружены");
            return assessment;
        }

        var saveResult = await ApplicationPersistence.SaveChangesAsync(dbContext, ct);
        if (!saveResult.IsSuccess)
        {
            return saveResult.Error!;
        }

        if (reconciliationResult.Value == ReceivingOrderReconciliation.Conflict)
        {
            logger.LogWarning(
                "При сверке приходного ордера с 1С обнаружены расхождения. Уровень: {Level}, поля: {Fields}",
                assessment.Level,
                assessment.Differences.Select(x => x.FieldCode).ToArray());
        }

        return assessment;
    }

    internal async Task<OperationResult> AcknowledgeSynchronizationAsync(
        ReceivingOrderImportSnapshot snapshot,
        string expectedFingerprint,
        string userId,
        CancellationToken ct = default)
    {
        await using ApplicationDbContext dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        ReceivingOrder? order = await dbContext.ReceivingOrders
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == snapshot.Id, ct);
        if (order is null)
        {
            return OperationError.NotFound($"Приходный ордер '{snapshot.Id}' не найден в WMS.");
        }

        OrderSynchronizationAssessment assessment =
            ReceivingOrderSynchronizationComparer.Compare(order, snapshot);
        if (!string.Equals(assessment.Fingerprint, expectedFingerprint, StringComparison.Ordinal))
        {
            OperationResult<ReceivingOrderReconciliation> reconciliationResult =
                order.Reconcile(snapshot, DateTimeOffset.UtcNow);
            if (!reconciliationResult.IsSuccess)
                return reconciliationResult.Error!;

            OperationResult saveResult = await ApplicationPersistence.SaveChangesAsync(dbContext, ct);
            if (!saveResult.IsSuccess)
                return saveResult;

            return OperationError.Conflict(
                "Приходный ордер в 1С изменился. Просмотрите новые расхождения.");
        }

        OperationResult acknowledgeResult = order.AcknowledgeSynchronization(
            snapshot,
            assessment,
            DateTimeOffset.UtcNow,
            userId);
        return acknowledgeResult.IsSuccess
            ? await ApplicationPersistence.SaveChangesAsync(dbContext, ct)
            : acknowledgeResult;
    }

    public async Task<OperationResult> SetInReceivingAsync(Guid orderId, string userId, CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        var result = await StageSetInReceivingAsync(dbContext, orderId, userId, ct);
        if (!result.IsSuccess)
        {
            return result;
        }

        return await ApplicationPersistence.SaveChangesAsync(dbContext, ct);
    }

    internal async Task<OperationResult> StageStartReceivingAsync(
        ApplicationDbContext dbContext,
        Guid orderId,
        Guid receivingLocationId,
        string userId,
        CancellationToken ct)
    {
        using var scope = logger.BeginScope("ReceivingOrder Start {OrderId}", orderId);
        using var activity = AppTracing.StartActivity(
            "ReceivingOrder.Start",
            nameof(ReceivingOrderCommandService));

        var order = await LoadOrderAsync(dbContext, orderId, ct);
        if (order is null)
        {
            logger.LogError("Приходный ордер {OrderId} не найден", orderId);
            return OperationError.NotFound($"Приходный ордер '{orderId}' не найден.");
        }

        OperationResult synchronizationResult = EnsureSynchronizationAllowsWork(order);
        if (!synchronizationResult.IsSuccess)
            return synchronizationResult;

        var locationResult = await StageSetReceivingLocationAsync(
            dbContext,
            order,
            receivingLocationId,
            ct);
        if (!locationResult.IsSuccess)
        {
            return locationResult;
        }

        return await StageSetInReceivingAsync(order, userId, ct);
    }

    internal async Task<OperationResult> StageSetInReceivingAsync(
        ApplicationDbContext dbContext,
        Guid orderId,
        string userId,
        CancellationToken ct)
    {
        using var scope = logger.BeginScope("ReceivingOrder SetInReceiving {OrderId}", orderId);
        using var activity = AppTracing.StartActivity(
            "ReceivingOrder.SetInReceiving",
            nameof(ReceivingOrderCommandService));

        var order = await LoadOrderAsync(dbContext, orderId, ct);
        if (order is null)
        {
            logger.LogError("Приходный ордер {OrderId} не найден", orderId);
            return OperationError.NotFound($"Приходный ордер '{orderId}' не найден.");
        }

        OperationResult synchronizationResult = EnsureSynchronizationAllowsWork(order);
        if (!synchronizationResult.IsSuccess)
            return synchronizationResult;

        return await StageSetInReceivingAsync(order, userId, ct);
    }

    private async Task<OperationResult> StageSetInReceivingAsync(
        ReceivingOrder order,
        string userId,
        CancellationToken ct)
    {
        var transitionResult = order.SetInReceiving(DateTimeOffset.UtcNow, userId);
        if (!transitionResult.IsSuccess)
        {
            logger.LogError("Не удалось перевести приходный ордер в приемку: {ErrorMessage}", transitionResult.Error?.Message);
            return transitionResult;
        }

        var externalResult = await outboundService.SetInReceivingAsync(order.Id, ct);

        if (!externalResult.IsSuccess)
        {
            logger.LogError("Не удалось перевести документ 1С в приемку: {ErrorMessage}", externalResult.Error?.Message);
            return externalResult;
        }

        return OperationResult.Success();
    }

    public async Task<OperationResult> SetReceivedAsync(Guid orderId, string userId, CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        var result = await StageSetReceivedAsync(dbContext, orderId, userId, ct);
        return result.IsSuccess
            ? await ApplicationPersistence.SaveChangesAsync(dbContext, ct)
            : result;
    }

    internal async Task<OperationResult> StageSetReceivedAsync(
        ApplicationDbContext dbContext,
        Guid orderId,
        string userId,
        CancellationToken ct)
    {
        using var scope = logger.BeginScope("ReceivingOrder SetReceived {OrderId}", orderId);
        using var activity = AppTracing.StartActivity(
            "ReceivingOrder.SetReceived",
            nameof(ReceivingOrderCommandService));

        var order = await LoadOrderAsync(dbContext, orderId, ct);
        if (order is null)
        {
            logger.LogError("Приходный ордер {OrderId} не найден", orderId);
            return OperationError.NotFound($"Приходный ордер '{orderId}' не найден.");
        }

        OperationResult synchronizationResult = await VerifyFreshSynchronizationAsync(
            dbContext,
            order,
            expectReceivedTarget: true,
            ct);
        if (!synchronizationResult.IsSuccess)
            return synchronizationResult;

        var now = DateTimeOffset.UtcNow;
        var transitionResult = order.SetReceived(now, userId);
        if (!transitionResult.IsSuccess)
        {
            logger.LogError("Не удалось завершить приемку приходного ордера: {ErrorMessage}", transitionResult.Error?.Message);
            return transitionResult;
        }

        var movementsResult = CreateReceivingMovements(order, now);
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

        if (order.HasPlanFactDifference)
        {
            var externalItemsUpdateResult = await outboundService.UpdateDocumentItemsAsync(
                order.Id,
                order.Items,
                ct);

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

        return OperationResult.Success();
    }

    private async Task<OperationResult> VerifyFreshSynchronizationAsync(
        ApplicationDbContext dbContext,
        ReceivingOrder order,
        bool expectReceivedTarget,
        CancellationToken ct)
    {
        OperationResult<ReceivingOrderImportSnapshot> snapshotResult =
            await orderSource.GetSnapshotAsync(order.Id, ct);
        if (!snapshotResult.IsSuccess)
            return snapshotResult.Error!;

        ReceivingOrderImportSnapshot snapshot = snapshotResult.Value!;
        OrderSynchronizationAssessment sourceAssessment =
            ReceivingOrderSynchronizationComparer.Compare(order, snapshot);
        OrderSynchronizationAssessment targetAssessment = expectReceivedTarget
            ? ReceivingOrderSynchronizationComparer.CompareReceivedTarget(order, snapshot)
            : sourceAssessment;
        OrderSynchronizationAssessment assessment = sourceAssessment.Level == OrderSynchronizationLevel.Synchronized
            ? sourceAssessment
            : targetAssessment.Level == OrderSynchronizationLevel.Synchronized
                ? targetAssessment
                : sourceAssessment;

        order.ApplySynchronizationAssessment(assessment, DateTimeOffset.UtcNow);
        OperationResult saveResult = await ApplicationPersistence.SaveChangesAsync(dbContext, ct);
        return saveResult.IsSuccess
            ? EnsureSynchronizationAllowsWork(order)
            : saveResult;
    }

    private static OperationResult EnsureSynchronizationAllowsWork(ReceivingOrder order) =>
        order.ExternalSynchronizationLevel switch
        {
            OrderSynchronizationLevel.Synchronized => OperationResult.Success(),
            OrderSynchronizationLevel.RequiresOperatorDecision => OperationError.Conflict(
                "Приходный ордер требует решения оператора по изменениям 1С."),
            _ => OperationError.Conflict(
                "Работа с приходным ордером заблокирована из-за расхождений с 1С.")
        };

    public async Task<OperationResult> UpdateOrderItemFactQuantityAsync(
        Guid receivingOrderId,
        int lineNumber,
        decimal factQuantity,
        string? comment,
        CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        var result = await StageUpdateItemFactQuantityAsync(
            dbContext,
            receivingOrderId,
            lineNumber,
            factQuantity,
            comment,
            ct);
        if (!result.IsSuccess)
        {
            return result;
        }

        return await ApplicationPersistence.SaveChangesAsync(dbContext, ct);
    }

    internal async Task<OperationResult> StageUpdateItemFactQuantityAsync(
        ApplicationDbContext dbContext,
        Guid orderId,
        int lineNumber,
        decimal factQuantity,
        string? comment,
        CancellationToken ct)
    {
        var order = await LoadOrderAsync(dbContext, orderId, ct);
        if (order is null)
        {
            return OperationError.NotFound($"Приходный ордер '{orderId}' не найден.");
        }

        return order.UpdateItemFact(lineNumber, factQuantity, comment);
    }

    internal async Task<OperationResult> StageIncrementItemFactAsync(
        ApplicationDbContext dbContext,
        Guid orderId,
        int lineNumber,
        CancellationToken ct)
    {
        var order = await LoadOrderAsync(dbContext, orderId, ct);
        if (order is null)
        {
            return OperationError.NotFound($"Приходный ордер '{orderId}' не найден.");
        }

        return order.IncrementItemFact(lineNumber);
    }

    internal async Task<OperationResult> StageSetItemFactQuantityAsync(
        ApplicationDbContext dbContext,
        Guid orderId,
        int lineNumber,
        decimal factQuantity,
        CancellationToken ct)
    {
        var order = await LoadOrderAsync(dbContext, orderId, ct);
        if (order is null)
        {
            return OperationError.NotFound($"Приходный ордер '{orderId}' не найден.");
        }

        return order.UpdateItemFactQuantity(lineNumber, factQuantity);
    }

    public async Task<OperationResult> UpdateOrderItemCommentAsync(
        Guid receivingOrderId,
        int lineNumber,
        string? comment,
        CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        var result = await StageUpdateItemCommentAsync(
            dbContext,
            receivingOrderId,
            lineNumber,
            comment,
            ct);
        if (!result.IsSuccess)
        {
            return result;
        }

        return await ApplicationPersistence.SaveChangesAsync(dbContext, ct);
    }

    internal async Task<OperationResult> StageUpdateItemCommentAsync(
        ApplicationDbContext dbContext,
        Guid orderId,
        int lineNumber,
        string? comment,
        CancellationToken ct)
    {
        var order = await LoadOrderAsync(dbContext, orderId, ct);
        if (order is null)
        {
            return OperationError.NotFound($"Приходный ордер '{orderId}' не найден.");
        }

        return order.UpdateItemComment(lineNumber, comment);
    }

    public async Task<OperationResult> SetReceivingLocationAsync(
        Guid receivingOrderId,
        Guid receivingLocationId,
        CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        var result = await StageSetReceivingLocationAsync(
            dbContext,
            receivingOrderId,
            receivingLocationId,
            ct);
        if (!result.IsSuccess)
        {
            return result;
        }

        return await ApplicationPersistence.SaveChangesAsync(dbContext, ct);
    }

    internal async Task<OperationResult> StageSetReceivingLocationAsync(
        ApplicationDbContext dbContext,
        Guid orderId,
        Guid receivingLocationId,
        CancellationToken ct)
    {
        var order = await LoadOrderAsync(dbContext, orderId, ct);
        if (order is null)
        {
            return OperationError.NotFound($"Приходный ордер '{orderId}' не найден.");
        }

        return await StageSetReceivingLocationAsync(
            dbContext,
            order,
            receivingLocationId,
            ct);
    }

    private static async Task<OperationResult> StageSetReceivingLocationAsync(
        ApplicationDbContext dbContext,
        ReceivingOrder order,
        Guid receivingLocationId,
        CancellationToken ct)
    {
        var location = await dbContext.StorageLocations
            .Include(x => x.Zone)
            .Include(x => x.ActiveLock)
            .SingleOrDefaultAsync(x => x.Id == receivingLocationId, ct);

        if (location is null
            || location.WarehouseId != order.WarehouseId
            || location.IsFolder
            || location.DeletionMark
            || location.Zone?.DeletionMark == true
            || location.Zone?.Type != ZoneType.Receiving)
        {
            return OperationError.Invalid("Позиция приёмки должна принадлежать зоне приёмки на складе ордера.");
        }

        var availabilityResult = StorageLocationAvailability.ValidateUnlocked(location);
        if (!availabilityResult.IsSuccess)
        {
            return availabilityResult;
        }

        var locationResult = order.SetReceivingLocation(receivingLocationId);
        if (!locationResult.IsSuccess)
        {
            return locationResult;
        }

        return OperationResult.Success();
    }

    private static Task<ReceivingOrder?> LoadOrderAsync(
        ApplicationDbContext dbContext,
        Guid orderId,
        CancellationToken ct) =>
        dbContext.ReceivingOrders
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == orderId, ct);

    private static OperationResult<List<InventoryMovement>> CreateReceivingMovements(
        ReceivingOrder order,
        DateTimeOffset createdAtUtc)
    {
        var movements = new List<InventoryMovement>();
        foreach (var item in order.Items.Where(x => x.FactQuantity > 0))
        {
            var movementResult = InventoryMovement.Create(
                Guid.NewGuid(),
                order.WarehouseId,
                null,
                order.ReceivingLocationId,
                item.StockKeepingUnitId,
                item.FactQuantity!.Value,
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

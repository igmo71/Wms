using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wms.Application.Persistence;
using Wms.Common;
using Wms.Data;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Application.ReceivingOrders;

public sealed class ReceivingOrderSynchronizationService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    IReceivingOrderSource orderSource,
    ILogger<ReceivingOrderSynchronizationService> logger)
{
    public async Task<OperationResult<OrderSynchronizationAssessment>> CheckAsync(
        Guid orderId,
        CancellationToken ct = default)
    {
        OperationResult<ReceivingOrderImportSnapshot> snapshotResult =
            await orderSource.GetSnapshotAsync(orderId, ct);
        return snapshotResult.IsSuccess
            ? await ApplySnapshotAsync(snapshotResult.Value!, allowCreate: false, ct)
            : snapshotResult.Error!;
    }

    public async Task<OperationResult<ReceivingCompletionReconciliation>> CheckCompletionAsync(
        Guid orderId,
        CancellationToken ct = default)
    {
        OperationResult<ReceivingOrderImportSnapshot> snapshotResult =
            await orderSource.GetSnapshotAsync(orderId, ct);
        if (!snapshotResult.IsSuccess)
            return snapshotResult.Error!;

        await using ApplicationDbContext dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        ReceivingOrder? order = await LoadCompletionOrderAsync(dbContext, orderId, ct);
        if (order is null)
            return OperationError.NotFound($"Приходный ордер '{orderId}' не найден в WMS.");

        ReceivingCompletionReconciliation reconciliation = ReceivingCompletionComparer.Compare(
            order,
            snapshotResult.Value!,
            DateTimeOffset.UtcNow);
        // Participation follows source synchronization, not unfinished LPN counting.
        order.ApplySynchronizationAssessment(
            order.AssessSynchronization(snapshotResult.Value!, reconciliation.CheckedAtUtc),
            reconciliation.CheckedAtUtc);
        OperationResult saveResult = await ApplicationPersistence.SaveChangesAsync(dbContext, ct);
        return saveResult.IsSuccess ? reconciliation : saveResult.Error!;
    }

    public async Task<OperationResult<OrderSynchronizationAssessment>> ImportNotificationAsync(
        Guid orderId,
        CancellationToken ct = default)
    {
        OperationResult<ReceivingOrderImportSnapshot> snapshotResult =
            await orderSource.GetSnapshotAsync(orderId, ct);
        return snapshotResult.IsSuccess
            ? await ApplySnapshotAsync(snapshotResult.Value!, allowCreate: true, ct)
            : snapshotResult.Error!;
    }

    public async Task<OperationResult> AcknowledgeAsync(
        Guid orderId,
        string expectedFingerprint,
        string userId,
        CancellationToken ct = default)
    {
        OperationResult<ReceivingOrderImportSnapshot> snapshotResult =
            await orderSource.GetSnapshotAsync(orderId, ct);
        if (!snapshotResult.IsSuccess)
            return snapshotResult.Error!;

        ReceivingOrderImportSnapshot snapshot = snapshotResult.Value!;
        await using ApplicationDbContext dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        ReceivingOrder? order = await dbContext.ReceivingOrders
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == snapshot.Id, ct);
        if (order is null)
            return OperationError.NotFound($"Приходный ордер '{snapshot.Id}' не найден в WMS.");

        DateTimeOffset now = DateTimeOffset.UtcNow;
        OrderSynchronizationAssessment assessment = order.AssessSynchronization(snapshot, now);
        if (!string.Equals(assessment.Fingerprint, expectedFingerprint, StringComparison.Ordinal))
        {
            OperationResult<ReceivingOrderReconciliation> reconciliationResult = order.Reconcile(snapshot, now);
            if (!reconciliationResult.IsSuccess)
                return reconciliationResult.Error!;

            OperationResult saveResult = await ApplicationPersistence.SaveChangesAsync(dbContext, ct);
            return saveResult.IsSuccess
                ? OperationError.Conflict("Приходный ордер в 1С изменился. Просмотрите новые расхождения.")
                : saveResult;
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

    internal async Task<OperationResult<ReceivingCompletionReconciliation>> PersistCompletionCheckpointAsync(
        ApplicationDbContext dbContext,
        ReceivingOrder order,
        CancellationToken ct)
    {
        OperationResult<ReceivingOrderImportSnapshot> snapshotResult =
            await orderSource.GetSnapshotAsync(order.Id, ct);
        if (!snapshotResult.IsSuccess)
            return snapshotResult.Error!;

        ReceivingCompletionReconciliation reconciliation = ReceivingCompletionComparer.Compare(
            order,
            snapshotResult.Value!,
            DateTimeOffset.UtcNow);
        // Keep the checkpoint source-only; the returned completion decision still
        // carries the source + LPN fingerprint and all completion blockers.
        order.ApplySynchronizationAssessment(
            order.AssessSynchronization(snapshotResult.Value!, reconciliation.CheckedAtUtc),
            reconciliation.CheckedAtUtc);
        OperationResult saveResult = await ApplicationPersistence.SaveChangesAsync(dbContext, ct);
        return saveResult.IsSuccess ? reconciliation : saveResult.Error!;
    }

    private static Task<ReceivingOrder?> LoadCompletionOrderAsync(
        ApplicationDbContext dbContext,
        Guid orderId,
        CancellationToken ct) =>
        dbContext.ReceivingOrders
            .Include(x => x.Items)
            .Include(x => x.Participants)
            .Include(x => x.LicensePlateNumbers)
                .ThenInclude(x => x.Items)
            .SingleOrDefaultAsync(x => x.Id == orderId, ct);

    private async Task<OperationResult<OrderSynchronizationAssessment>> ApplySnapshotAsync(
        ReceivingOrderImportSnapshot snapshot,
        bool allowCreate,
        CancellationToken ct)
    {
        using IDisposable? scope = logger.BeginScope("ReceivingOrder Synchronize {OrderId}", snapshot.Id);
        using System.Diagnostics.Activity? activity = AppTracing.StartActivity(
            "ReceivingOrder.Synchronize",
            nameof(ReceivingOrderSynchronizationService));

        await using ApplicationDbContext dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        ReceivingOrder? existingOrder = await dbContext.ReceivingOrders
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == snapshot.Id, ct);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (existingOrder is null)
        {
            if (!allowCreate)
                return OperationError.NotFound($"Приходный ордер '{snapshot.Id}' не найден в WMS.");

            OperationResult<ReceivingOrder> creationResult = ReceivingOrder.Create(snapshot, now);
            if (!creationResult.IsSuccess)
                return creationResult.Error!;

            ReceivingOrder createdOrder = creationResult.Value!;
            dbContext.ReceivingOrders.Add(createdOrder);
            OperationResult saveCreationResult = await ApplicationPersistence.SaveChangesAsync(dbContext, ct);
            return saveCreationResult.IsSuccess
                ? ReceivingOrderSynchronizationComparer.Compare(createdOrder, snapshot)
                : saveCreationResult.Error!;
        }

        OperationResult<ReceivingOrderReconciliation> reconciliationResult = existingOrder.Reconcile(snapshot, now);
        if (!reconciliationResult.IsSuccess)
            return reconciliationResult.Error!;

        OrderSynchronizationAssessment assessment = existingOrder.AssessSynchronization(snapshot, now);
        if (reconciliationResult.Value == ReceivingOrderReconciliation.Unchanged)
        {
            logger.LogDebug("Изменения документа в 1С не обнаружены");
            return assessment;
        }

        OperationResult saveResult = await ApplicationPersistence.SaveChangesAsync(dbContext, ct);
        if (!saveResult.IsSuccess)
            return saveResult.Error!;

        if (reconciliationResult.Value == ReceivingOrderReconciliation.DifferencesDetected)
        {
            logger.LogWarning(
                "При сверке приходного ордера с 1С обнаружены расхождения. Уровень: {Level}, поля: {Fields}",
                assessment.Level,
                assessment.Differences.Select(x => x.FieldCode).ToArray());
        }

        return assessment;
    }

    private static OperationResult EnsureAllowsWork(ReceivingOrder order) =>
        order.SynchronizationLevel switch
        {
            OrderSynchronizationLevel.Synchronized => OperationResult.Success(),
            OrderSynchronizationLevel.RequiresOperatorDecision => OperationError.Conflict(
                "Приходный ордер требует решения оператора по изменениям 1С."),
            _ => OperationError.Conflict(
                "Работа с приходным ордером заблокирована из-за расхождений с 1С.")
        };
}

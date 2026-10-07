using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wms.Application.Commands;
using Wms.Application.Inventory.Movements;
using System.Text.Json;
using Wms.Common;
using Wms.Data;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Application.ReceivingOrders;

public class ReceivingOrderCommandService(
    CommandExecutor commandExecutor,
    InventoryPostingService inventoryPostingService,
    ReceivingOrderSynchronizationService synchronizationService,
    ILogger<ReceivingOrderCommandService> logger)
{
    // Persisted protocol identifiers: do not derive these from CLR type names.
    private const string JoinReceivingCommandType = "receiving-order.join-receiving";
    private const string CompleteReceivingCommandType = "receiving-order.complete-receiving";

    public Task<OperationResult<Guid>> StartReceivingAsync(
        StartReceivingCommand command,
        CommandContext context,
        CancellationToken ct = default) => Task.FromResult<OperationResult<Guid>>(
            OperationError.Invalid(
                "Прежний запуск приёмки отключён. Используйте присоединение к ордеру."));

    public Task<OperationResult<Guid>> JoinReceivingAsync(
        JoinReceivingOrderCommand command,
        CommandContext context,
        CancellationToken ct = default) =>
        commandExecutor.ExecuteAsync(
            JoinReceivingCommandType,
            context.RequestId,
            CommandExecutor.ComputeHash(
                command.ReceivingLocationId is Guid locationId
                    ? $"{command.OrderId:N}|{locationId:N}"
                    : command.OrderId.ToString("N")),
            context.UserId,
            async (dbContext, token) =>
            {
                var order = await dbContext.ReceivingOrders
                    .Include(x => x.Items)
                    .Include(x => x.Participants)
                    .FirstOrDefaultAsync(x => x.Id == command.OrderId, token);
                if (order is null)
                    return OperationError.NotFound(
                        $"Приходный ордер '{command.OrderId}' не найден.");

                OperationResult synchronizationResult = EnsureSynchronizationAllowsWork(order);
                if (!synchronizationResult.IsSuccess)
                    return synchronizationResult.Error!;

                OperationResult eligibility = order.ValidateReceivingParticipationEligibility();
                if (!eligibility.IsSuccess)
                    return eligibility.Error!;

                if (order.Status == ReceivingOrderStatus.ReadyForReceiving)
                {
                    if (command.ReceivingLocationId is not Guid receivingLocationId)
                        return OperationError.Invalid(
                            "Первый участник должен отсканировать ячейку приёмки.");

                    OperationResult locationResult = await SetReceivingLocationAsync(
                        dbContext, order, receivingLocationId, token);
                    if (!locationResult.IsSuccess)
                        return locationResult.Error!;

                    OperationResult startResult = order.SetInReceiving(
                        DateTimeOffset.UtcNow,
                        context.UserId);
                    if (!startResult.IsSuccess)
                        return startResult.Error!;
                }
                else if (order.Status != ReceivingOrderStatus.InReceiving)
                {
                    return OperationError.Invalid(
                        "Присоединиться можно только к готовому или находящемуся в работе ордеру.");
                }

                bool wasParticipant = order.Participants.Any(x => x.UserId == context.UserId);
                OperationResult joinResult = order.JoinReceiving(
                    DateTimeOffset.UtcNow,
                    context.UserId);
                if (joinResult.IsSuccess && !wasParticipant)
                {
                    dbContext.ReceivingOrderParticipants.Add(
                        order.Participants.Single(x => x.UserId == context.UserId));
                }
                return joinResult.IsSuccess ? order.Id : joinResult.Error!;
            },
            ct);

    public Task<OperationResult<Guid>> CompleteReceivingAsync(
        CompleteReceivingCommand command,
        CommandContext context,
        bool canApproveQuantityDifference = false,
        CancellationToken ct = default) =>
        commandExecutor.ExecuteAsync(
            CompleteReceivingCommandType,
            context.RequestId,
            CommandExecutor.ComputeHash(JsonSerializer.Serialize(command)),
            context.UserId,
            async (dbContext, token) =>
            {
                var result = await CompleteReceivingWithCheckpointAsync(
                    dbContext, command, context.UserId, canApproveQuantityDifference, token);
                return result.IsSuccess ? command.OrderId : result.Error!;
            },
            ct);

    private async Task<OperationResult> CompleteReceivingWithCheckpointAsync(
        ApplicationDbContext dbContext,
        CompleteReceivingCommand command,
        string userId,
        bool canApproveQuantityDifference,
        CancellationToken ct)
    {
        Guid orderId = command.OrderId;
        Guid? receivingLocationId = command.ReceivingLocationId;
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

        // Explicit independent checkpoint, after receipt lookup and before final effects.
        OperationResult<ReceivingCompletionReconciliation> synchronizationResult = await synchronizationService.PersistCompletionCheckpointAsync(
            dbContext,
            order,
            ct);
        if (!synchronizationResult.IsSuccess)
            return synchronizationResult.Error!;

        ReceivingCompletionReconciliation reconciliation = synchronizationResult.Value!;
        if (!string.Equals(
                reconciliation.Assessment.Fingerprint,
                command.ExpectedFingerprint,
                StringComparison.Ordinal))
        {
            return OperationError.Conflict(
                "План 1С или состав LPN изменился. Просмотрите свежую сверку и подтвердите завершение заново.");
        }
        if (reconciliation.IsBlocking)
            return OperationError.Conflict("Завершение заблокировано свежей сверкой с 1С.");

        return await CompleteReceivingCoreAsync(
            dbContext,
            order,
            reconciliation,
            receivingLocationId,
            userId,
            canApproveQuantityDifference,
            command.ManagerReason,
            ct);
    }

    private async Task<OperationResult> CompleteReceivingCoreAsync(
        ApplicationDbContext dbContext,
        ReceivingOrder order,
        ReceivingCompletionReconciliation reconciliation,
        Guid? receivingLocationId,
        string userId,
        bool canApproveQuantityDifference,
        string? managerReason,
        CancellationToken ct)
    {
        if (receivingLocationId is Guid selectedLocationId)
        {
            var setLocationResult = await SetReceivingLocationAsync(
                dbContext,
                order,
                selectedLocationId,
                ct);
            if (!setLocationResult.IsSuccess)
                return setLocationResult;
        }

        var locationResult = await ReceivingOrderLocationPolicy.RequireReceivingLocationAsync(
            dbContext,
            order,
            order.ReceivingLocationId,
            ct);
        if (!locationResult.IsSuccess)
            return locationResult;

        var now = DateTimeOffset.UtcNow;
        var transitionResult = order.SetReceived(
            reconciliation,
            JsonSerializer.Serialize(reconciliation.OriginalPlan),
            JsonSerializer.Serialize(reconciliation.CurrentPlan),
            JsonSerializer.Serialize(reconciliation.Fact),
            JsonSerializer.Serialize(reconciliation.Assessment.Differences),
            now,
            userId,
            canApproveQuantityDifference,
            managerReason);
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

        return OperationResult.Success();
    }

    private static OperationResult EnsureSynchronizationAllowsWork(ReceivingOrder order) =>
        order.SynchronizationLevel switch
        {
            OrderSynchronizationLevel.Synchronized => OperationResult.Success(),
            OrderSynchronizationLevel.RequiresOperatorDecision => OperationError.Conflict(
                "Приходный ордер требует решения оператора по изменениям 1С."),
            _ => OperationError.Conflict(
                "Работа с приходным ордером заблокирована из-за расхождений с 1С.")
        };

    private static async Task<OperationResult> SetReceivingLocationAsync(
        ApplicationDbContext dbContext,
        ReceivingOrder order,
        Guid receivingLocationId,
        CancellationToken ct)
    {
        var validationResult = await ReceivingOrderLocationPolicy.RequireReceivingLocationAsync(
            dbContext,
            order,
            receivingLocationId,
            ct);
        if (!validationResult.IsSuccess)
        {
            return validationResult;
        }

        return order.SetReceivingLocation(receivingLocationId);
    }

    private static Task<ReceivingOrder?> LoadOrderAsync(
        ApplicationDbContext dbContext,
        Guid orderId,
        CancellationToken ct) =>
        dbContext.ReceivingOrders
            .Include(x => x.Items)
            .Include(x => x.Participants)
            .Include(x => x.LicensePlateNumbers)
                .ThenInclude(x => x.Items)
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

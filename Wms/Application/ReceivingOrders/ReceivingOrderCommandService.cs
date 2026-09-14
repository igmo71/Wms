using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wms.Application.Commands;
using Wms.Application.Inventory.Movements;
using System.Globalization;
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
    IReceivingOrderExecutionSink executionSink,
    ILogger<ReceivingOrderCommandService> logger)
{
    // Persisted protocol identifiers: do not derive these from CLR type names.
    private const string StartReceivingCommandType = "receiving-order.start-receiving";
    private const string CompleteReceivingCommandType = "receiving-order.complete-receiving";

    public Task<OperationResult<Guid>> StartReceivingAsync(
        StartReceivingCommand command,
        CommandContext context,
        CancellationToken ct = default) =>
        commandExecutor.ExecuteAsync(
            StartReceivingCommandType,
            context.RequestId,
            CommandExecutor.ComputeHash($"{command.OrderId:N}|{command.ReceivingLocationId:N}"),
            context.UserId,
            async (dbContext, token) =>
            {
                var result = await StartReceivingCoreAsync(dbContext, command, context.UserId, token);
                return result.IsSuccess ? command.OrderId : result.Error!;
            },
            ct);

    private async Task<OperationResult> StartReceivingCoreAsync(
        ApplicationDbContext dbContext,
        StartReceivingCommand command,
        string userId,
        CancellationToken ct)
    {
        var (orderId, receivingLocationId) = command;
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

        var locationResult = await SetReceivingLocationAsync(
            dbContext,
            order,
            receivingLocationId,
            ct);
        if (!locationResult.IsSuccess)
        {
            return locationResult;
        }

        var transitionResult = order.SetInReceiving(DateTimeOffset.UtcNow, userId);
        if (!transitionResult.IsSuccess)
        {
            logger.LogError("Не удалось перевести приходный ордер в приемку: {ErrorMessage}", transitionResult.Error?.Message);
            return transitionResult;
        }

        if (order.IntegrationMode == ReceivingIntegrationMode.Autonomous)
            return OperationResult.Success();

        var externalResult = await executionSink.SetInReceivingAsync(order.Id, ct);

        if (!externalResult.IsSuccess)
        {
            logger.LogError("Не удалось перевести документ 1С в приемку: {ErrorMessage}", externalResult.Error?.Message);
            return externalResult;
        }

        return OperationResult.Success();
    }

    public Task<OperationResult<Guid>> CompleteReceivingAsync(
        CompleteReceivingCommand command,
        CommandContext context,
        CancellationToken ct = default) =>
        commandExecutor.ExecuteAsync(
            CompleteReceivingCommandType,
            context.RequestId,
            // Null keeps the original Mobile completion hash, independent of DB state.
            CommandExecutor.ComputeHash(command.ReceivingLocationId is Guid locationId
                ? $"{command.OrderId:N}|{locationId:N}"
                : command.OrderId.ToString("N")),
            context.UserId,
            async (dbContext, token) =>
            {
                var result = await CompleteReceivingWithCheckpointAsync(
                    dbContext, command, context.UserId, token);
                return result.IsSuccess ? command.OrderId : result.Error!;
            },
            ct);

    private async Task<OperationResult> CompleteReceivingWithCheckpointAsync(
        ApplicationDbContext dbContext,
        CompleteReceivingCommand command,
        string userId,
        CancellationToken ct)
    {
        var (orderId, receivingLocationId) = command;
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
        OperationResult synchronizationResult = await synchronizationService.PersistCompletionCheckpointAsync(
            dbContext,
            order,
            ct);
        if (!synchronizationResult.IsSuccess)
            return synchronizationResult;

        return await CompleteReceivingCoreAsync(dbContext, order, receivingLocationId, userId, ct);
    }

    private async Task<OperationResult> CompleteReceivingCoreAsync(
        ApplicationDbContext dbContext,
        ReceivingOrder order,
        Guid? receivingLocationId,
        string userId,
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

        if (order.IntegrationMode == ReceivingIntegrationMode.Autonomous)
            return OperationResult.Success();

        if (order.SourceSnapshot is not { } source || order.Items.Any(item =>
            source.Items.SingleOrDefault(x => x.LineNumber == item.LineNumber
                && x.StockKeepingUnitId == item.StockKeepingUnitId)?.Quantity != item.FactQuantity))
        {
            var externalItemsUpdateResult = await executionSink.UpdateItemsAsync(
                order.Id,
                order.Items,
                ct);

            if (!externalItemsUpdateResult.IsSuccess)
            {
                logger.LogError("Не удалось обновить строки приходного ордера в 1С: {ErrorMessage}", externalItemsUpdateResult.Error?.Message);
                return externalItemsUpdateResult;
            }
        }

        var externalResult = await executionSink.SetReceivedAsync(order.Id, ct);

        if (!externalResult.IsSuccess)
        {
            logger.LogError("Не удалось завершить приемку документа в 1С: {ErrorMessage}", externalResult.Error?.Message);
            return externalResult;
        }

        return OperationResult.Success();
    }

    public Task<OperationResult<Guid>> CompleteWithDiscrepanciesAsync(
        CompleteReceivingWithDiscrepanciesCommand command, CommandContext context,
        CancellationToken ct = default) =>
        commandExecutor.ExecuteAsync("receiving-order.complete-with-discrepancies",
            context.RequestId, CommandExecutor.ComputeHash(JsonSerializer.Serialize(command)), context.UserId,
            async (dbContext, token) =>
            {
                bool allowed = await (from user in dbContext.Users
                    join membership in dbContext.UserRoles on user.Id equals membership.UserId
                    join role in dbContext.Roles on membership.RoleId equals role.Id
                    where user.Id == context.UserId && user.EmailConfirmed
                        && (user.LockoutEnd == null || user.LockoutEnd <= DateTimeOffset.UtcNow)
                        && (role.Name == ApplicationRoles.Manager || role.Name == ApplicationRoles.Administrator)
                    select user.Id).AnyAsync(token);
                if (!allowed)
                    return OperationError.Invalid("Завершение с расхождениями доступно только заведующему или администратору.");
                if (string.IsNullOrWhiteSpace(command.Reason) || command.Reason.Trim().Length > 2000)
                    return OperationError.Invalid("Укажите причину завершения (до 2000 символов).");
                var order = await LoadOrderAsync(dbContext, command.OrderId, token);
                if (order is null) return OperationError.NotFound("Приходный ордер не найден.");
                if (order.OperationalRevision != command.ExpectedRevision)
                    return OperationError.Conflict("Ордер изменился. Обновите данные и повторно рассмотрите расхождения.");
                var checkpoint = await synchronizationService.PersistCompletionCheckpointAsync(
                    dbContext, order, token, managerCompletion: true);
                if (!checkpoint.IsSuccess) return checkpoint.Error!;
                if (order.SynchronizationFingerprint != command.ExpectedSourceFingerprint)
                    return OperationError.Conflict("Документ 1С изменился. Обновите данные и повторно рассмотрите расхождения.");
                var source = order.SourceSnapshot!;
                var assessment = order.AssessSynchronization(source, DateTimeOffset.UtcNow);
                var result = await CompleteReceivingCoreAsync(dbContext, order,
                    command.ReceivingLocationId, context.UserId, token);
                if (!result.IsSuccess) return result.Error!;
                order.RecordCompletionDecision(command.Reason, source, assessment);
                return order.Id;
            }, ct);

    private static OperationResult EnsureSynchronizationAllowsWork(ReceivingOrder order) =>
        order.CanStartWithSourceDifferences ? OperationResult.Success() : order.SynchronizationLevel switch
        {
            OrderSynchronizationLevel.Synchronized => OperationResult.Success(),
            OrderSynchronizationLevel.RequiresOperatorDecision => OperationError.Conflict(
                "Приходный ордер требует решения оператора по изменениям 1С."),
            _ => OperationError.Conflict(
                "Работа с приходным ордером заблокирована из-за расхождений с 1С.")
        };

    private const string IncrementFactCommandType = "receiving-order.increment-fact";
    private const string SetFactCommandType = "receiving-order.set-fact";
    private const string SetItemCommentCommandType = "receiving-order.set-item-comment";

    public Task<OperationResult<Guid>> IncrementItemFactAsync(
        IncrementReceivingFactCommand command, CommandContext context, CancellationToken ct = default) =>
        ExecuteItemActionAsync(IncrementFactCommandType, command.OrderId, context,
            CommandExecutor.ComputeHash($"{command.OrderId:N}|{command.LineNumber.ToString(CultureInfo.InvariantCulture)}"),
            order => order.IncrementItemFact(command.LineNumber), ct);

    public Task<OperationResult<Guid>> SetItemFactQuantityAsync(
        SetReceivingFactCommand command, CommandContext context, CancellationToken ct = default) =>
        ExecuteItemActionAsync(SetFactCommandType, command.OrderId, context,
            CommandExecutor.ComputeHash($"{command.OrderId:N}|{command.LineNumber.ToString(CultureInfo.InvariantCulture)}|{command.FactQuantity.ToString("G29", CultureInfo.InvariantCulture)}"),
            order => order.UpdateItemFactQuantity(command.LineNumber, command.FactQuantity), ct);

    public Task<OperationResult<Guid>> SetItemCommentAsync(
        SetReceivingItemCommentCommand command, CommandContext context, CancellationToken ct = default) =>
        ExecuteItemActionAsync(SetItemCommentCommandType, command.OrderId, context,
            // JSON distinguishes null/empty and escapes arbitrary separators in original input.
            CommandExecutor.ComputeHash($"{command.OrderId:N}|{command.LineNumber.ToString(CultureInfo.InvariantCulture)}|{JsonSerializer.Serialize(command.Comment)}"),
            order => order.UpdateItemComment(command.LineNumber, command.Comment), ct);

    private Task<OperationResult<Guid>> ExecuteItemActionAsync(
        string commandType, Guid orderId, CommandContext context, string requestHash,
        Func<ReceivingOrder, OperationResult> action, CancellationToken ct) =>
        commandExecutor.ExecuteAsync(commandType, context.RequestId, requestHash, context.UserId,
            async (dbContext, token) =>
            {
                var order = await LoadOrderAsync(dbContext, orderId, token);
                if (order is null)
                    return OperationError.NotFound($"Приходный ордер '{orderId}' не найден.");
                var result = action(order);
                return result.IsSuccess ? orderId : result.Error!;
            }, ct);

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

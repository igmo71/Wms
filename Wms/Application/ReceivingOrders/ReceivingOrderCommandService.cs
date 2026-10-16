using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wms.Application.Commands;
using Wms.Application.Inventory.Movements;
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
        Task.FromResult<OperationResult<Guid>>(OperationError.Conflict(
            "Старое завершение приёмки отключено. Приход проводится при закрытии LPN в новом сценарии."));

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

}

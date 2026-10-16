using Wms.Application.Authorization;
using Wms.Application.CommandExecution;
using Wms.Common;

namespace Wms.Application.Receiving;

public sealed record ReopenReceivingOrderRequest(
    Guid RequestId, Guid OrderId, string UserId, bool FromWeb);

public sealed class ReopenReceivingOrder(
    CommandExecutor executor, IReceivingWorkStore store,
    WarehousePermission permission, TimeProvider clock)
{
    private const string CommandType = "receiving.order.reopen-cycle";

    public Task<OperationResult<Guid>> ExecuteAsync(
        ReopenReceivingOrderRequest request, CancellationToken ct = default)
    {
        if (request is null || request.OrderId == Guid.Empty)
            return Task.FromResult<OperationResult<Guid>>(
                OperationError.Invalid("Приходный ордер обязателен."));

        var hash = CommandExecutor.ComputeHash($"{request.OrderId:N}:{request.FromWeb}");
        return executor.ExecuteAsync(CommandType, request.RequestId, hash, request.UserId,
            async (session, operationCt) =>
            {
                var allowed = await permission.RequireManagerAsync(request.UserId, operationCt);
                if (!allowed.IsSuccess)
                    return allowed.Error!;
                if (!request.FromWeb)
                    return OperationError.Conflict("Повторное открытие доступно Manager в Web.");

                var order = await store.LoadOrderAsync(session, request.OrderId, operationCt);
                if (order is null)
                    return OperationError.NotFound("Приходный ордер не найден.");
                var previousCycleNumber = order.ActiveCycleNumber;
                var now = clock.GetUtcNow();
                var reopened = order.ReopenForNextCycle(now);
                if (!reopened.IsSuccess)
                    return reopened.Error!;
                session.Stage(reopened.Value!);

                var audit = ReceivingWorkAudit.Create(
                    "receiving.order.cycle-reopened", now, request.UserId,
                    order.Id, null, null,
                    new { Status = "Received", CycleNumber = previousCycleNumber },
                    new { Status = "ReadyForReceiving", CycleId = reopened.Value!.Id,
                        CycleNumber = reopened.Value.Number },
                    CommandType, request.RequestId);
                if (!audit.IsSuccess)
                    return audit.Error!;
                return new CommandExecutionResult(order.Id, [audit.Value!]);
            }, ct);
    }
}

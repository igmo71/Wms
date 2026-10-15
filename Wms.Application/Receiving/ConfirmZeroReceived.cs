using Wms.Application.Authorization;
using Wms.Application.CommandExecution;
using Wms.Common;

namespace Wms.Application.Receiving;

public sealed record ConfirmZeroReceivedRequest(
    Guid RequestId, Guid OrderId, Guid SkuId, string UserId);

public sealed class ConfirmZeroReceived(
    CommandExecutor executor, IReceivingWorkStore store,
    WarehousePermission permission, TimeProvider clock)
{
    private const string CommandType = "receiving.order.confirm-zero";

    public Task<OperationResult<Guid>> ExecuteAsync(
        ConfirmZeroReceivedRequest request, CancellationToken ct = default)
    {
        if (request is null || request.OrderId == Guid.Empty || request.SkuId == Guid.Empty)
            return Task.FromResult<OperationResult<Guid>>(
                OperationError.Invalid("Ордер и SKU обязательны."));

        var hash = CommandExecutor.ComputeHash($"{request.OrderId:N}:{request.SkuId:N}");
        return executor.ExecuteAsync(CommandType, request.RequestId, hash, request.UserId,
            async (session, operationCt) =>
            {
                var access = await permission.RequireReceivingWorkerAsync(
                    request.UserId, operationCt);
                if (!access.IsSuccess)
                    return access.Error!;
                var order = await store.LoadOrderAsync(session, request.OrderId, operationCt);
                if (order is null)
                    return OperationError.NotFound("Приходный ордер не найден.");
                var previous = order.Items.SingleOrDefault(x => x.StockKeepingUnitId == request.SkuId)
                    ?.FactQuantity;
                var confirmed = order.ConfirmZeroReceived(request.SkuId, request.UserId);
                if (!confirmed.IsSuccess)
                    return confirmed.Error!;
                var audit = ReceivingWorkAudit.Create(
                    confirmed.Value ? "receiving.order.zero-confirmed"
                        : "receiving.order.zero-already-confirmed",
                    clock.GetUtcNow(), request.UserId, order.Id, null, request.SkuId,
                    new { FactQuantity = previous }, new { FactQuantity = 0m },
                    CommandType, request.RequestId);
                if (!audit.IsSuccess)
                    return audit.Error!;
                return new CommandExecutionResult(request.SkuId, [audit.Value!]);
            }, ct);
    }
}

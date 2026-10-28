using Wms.Application.Authorization;
using Wms.Application.CommandExecution;
using Wms.Common;

namespace Wms.Application.Receiving;

public sealed record CancelReceivingLpnRequest(
    Guid RequestId, Guid OrderId, Guid LpnId, string UserId);

public sealed class CancelReceivingLpn(
    CommandExecutor executor, IReceivingWorkStore store,
    WarehousePermission permission, TimeProvider clock)
{
    private const string CommandType = "receiving.lpn.cancel";

    public Task<OperationResult<Guid>> ExecuteAsync(
        CancelReceivingLpnRequest request, CancellationToken ct = default)
    {
        if (request is null || request.OrderId == Guid.Empty || request.LpnId == Guid.Empty)
            return Task.FromResult<OperationResult<Guid>>(
                OperationError.Invalid("Ордер и LPN обязательны."));

        var hash = CommandExecutor.ComputeHash($"{request.OrderId:N}:{request.LpnId:N}");
        return executor.ExecuteAsync(CommandType, request.RequestId, hash, request.UserId,
            async (session, operationCt) =>
            {
                var access = await permission.RequireOperatorAsync(
                    request.UserId, operationCt);
                if (!access.IsSuccess)
                    return access.Error!;
                var canManage = (await permission.RequireManagerAsync(
                    request.UserId, operationCt)).IsSuccess;
                var order = await store.LoadOrderAsync(session, request.OrderId, operationCt);
                if (order is null)
                    return OperationError.NotFound("Приходный ордер не найден.");
                var lpn = await store.LoadLpnAsync(session, request.LpnId, operationCt);
                if (lpn is null)
                    return OperationError.NotFound("LPN не найден.");

                var before = lpn.Items.Where(x => !x.IsDeleted)
                    .Select(x => new { x.StockKeepingUnitId, x.Quantity, x.ExpirationDate })
                    .ToArray();
                var now = clock.GetUtcNow();
                var cancelled = order.CancelReceivingLpn(lpn, request.UserId, now, canManage);
                if (!cancelled.IsSuccess)
                    return cancelled.Error!;
                var audit = ReceivingWorkAudit.Create("receiving.lpn.cancelled", now,
                    request.UserId, order.Id, lpn.Id, null,
                    new { Status = "Open", Items = before },
                    new { Status = "Cancelled", lpn.CancelledBy, lpn.CancelledAtUtc },
                    CommandType, request.RequestId);
                if (!audit.IsSuccess)
                    return audit.Error!;
                return new CommandExecutionResult(lpn.Id, [audit.Value!]);
            }, ct);
    }
}

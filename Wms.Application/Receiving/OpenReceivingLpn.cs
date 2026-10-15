using Wms.Application.Authorization;
using Wms.Application.CommandExecution;
using Wms.Common;

namespace Wms.Application.Receiving;

public sealed record OpenReceivingLpnRequest(
    Guid RequestId, Guid OrderId, string LpnCode, string UserId);

public sealed class OpenReceivingLpn(
    CommandExecutor executor, IReceivingWorkStore store,
    WarehousePermission permission, TimeProvider clock)
{
    private const string CommandType = "receiving.lpn.open";

    public Task<OperationResult<Guid>> ExecuteAsync(
        OpenReceivingLpnRequest request, CancellationToken ct = default)
    {
        if (request is null || request.OrderId == Guid.Empty
            || string.IsNullOrWhiteSpace(request.LpnCode))
            return Task.FromResult<OperationResult<Guid>>(
                OperationError.Invalid("Ордер и код LPN обязательны."));

        var code = request.LpnCode.Trim().ToUpperInvariant();
        var hash = CommandExecutor.ComputeHash($"{request.OrderId:N}:{code}");
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
                var lpn = await store.LoadLpnAsync(session, code, operationCt);
                if (lpn is null)
                    return OperationError.NotFound("Этикетка LPN не найдена.");

                var now = clock.GetUtcNow();
                var opened = order.OpenReceivingLpn(lpn, request.UserId, now);
                if (!opened.IsSuccess)
                    return opened.Error!;
                var audit = ReceivingWorkAudit.Create(
                    opened.Value ? "receiving.lpn.opened" : "receiving.lpn.already-associated",
                    now, request.UserId, order.Id, lpn.Id, null, null,
                    new { lpn.ReceivingCycleId, lpn.OriginalReceivingLocationId,
                        lpn.ResponsibleUserId, Status = lpn.Status.ToString() },
                    CommandType, request.RequestId);
                if (!audit.IsSuccess)
                    return audit.Error!;
                return new CommandExecutionResult(lpn.Id, [audit.Value!]);
            }, ct);
    }
}

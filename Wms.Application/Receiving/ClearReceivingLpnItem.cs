using Wms.Application.Authorization;
using Wms.Application.CommandExecution;
using Wms.Common;

namespace Wms.Application.Receiving;

public sealed record ClearReceivingLpnItemRequest(
    Guid RequestId, Guid OrderId, Guid LpnId, long ExpectedRevision, string UserId);

public sealed class ClearReceivingLpnItem(
    CommandExecutor executor, IReceivingWorkStore store,
    WarehousePermission permission, TimeProvider clock)
{
    private const string CommandType = "receiving.lpn.item.clear";

    public Task<OperationResult<Guid>> ExecuteAsync(
        ClearReceivingLpnItemRequest request, CancellationToken ct = default)
    {
        if (request is null || request.OrderId == Guid.Empty || request.LpnId == Guid.Empty
            || request.ExpectedRevision <= 0)
            return Task.FromResult<OperationResult<Guid>>(
                OperationError.Invalid("Ордер, LPN и версия LPN обязательны."));

        var hash = CommandExecutor.ComputeHash(
            $"{request.OrderId:N}:{request.LpnId:N}:{request.ExpectedRevision}");
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
                var item = lpn.Items.FirstOrDefault(x => !x.IsDeleted);
                var before = item is null ? null : new
                {
                    item.StockKeepingUnitId, item.Quantity, item.ExpirationDate
                };
                var now = clock.GetUtcNow();
                var changed = order.ClearReceivingLpnItem(lpn, request.ExpectedRevision,
                    request.UserId, now, canManage);
                if (!changed.IsSuccess)
                    return changed.Error!;
                var audit = ReceivingWorkAudit.Create("receiving.lpn.item.cleared", now,
                    request.UserId, order.Id, lpn.Id, item!.StockKeepingUnitId,
                    before, null, CommandType, request.RequestId);
                if (!audit.IsSuccess)
                    return audit.Error!;
                return new CommandExecutionResult(lpn.Id, [audit.Value!]);
            }, ct);
    }
}

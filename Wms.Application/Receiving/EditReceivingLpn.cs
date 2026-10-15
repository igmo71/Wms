using Wms.Application.Authorization;
using Wms.Application.CommandExecution;
using Wms.Common;

namespace Wms.Application.Receiving;

public sealed record EditReceivingLpnRequest(
    Guid RequestId, Guid OrderId, Guid LpnId, Guid SkuId,
    decimal Quantity, DateOnly? ExpirationDate, string UserId);

public sealed class EditReceivingLpn(
    CommandExecutor executor, IReceivingWorkStore store,
    WarehousePermission permission, TimeProvider clock)
{
    private const string CommandType = "receiving.lpn.edit";

    public Task<OperationResult<Guid>> ExecuteAsync(
        EditReceivingLpnRequest request, CancellationToken ct = default)
    {
        if (request is null || request.OrderId == Guid.Empty || request.LpnId == Guid.Empty
            || request.SkuId == Guid.Empty)
            return Task.FromResult<OperationResult<Guid>>(
                OperationError.Invalid("Ордер, LPN и SKU обязательны."));

        var hash = CommandExecutor.ComputeHash(FormattableString.Invariant(
            $"{request.OrderId:N}:{request.LpnId:N}:{request.SkuId:N}:{request.Quantity}:{request.ExpirationDate:yyyy-MM-dd}"));
        return executor.ExecuteAsync(CommandType, request.RequestId, hash, request.UserId,
            async (session, operationCt) =>
            {
                var access = await permission.RequireReceivingWorkerAsync(
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
                var previous = lpn.Items.SingleOrDefault(x => !x.IsDeleted);
                var before = previous is null ? null : new
                {
                    previous.StockKeepingUnitId, previous.Quantity, previous.ExpirationDate
                };
                var now = clock.GetUtcNow();
                var changed = order.EditReceivingLpn(lpn, request.SkuId,
                    request.ExpirationDate, request.Quantity, request.UserId, now, canManage);
                if (!changed.IsSuccess)
                    return changed.Error!;
                var activeItem = lpn.Items.Single(x => !x.IsDeleted);
                if (previous?.Id != activeItem.Id)
                    session.Stage(activeItem);
                var audit = ReceivingWorkAudit.Create("receiving.lpn.edited", now,
                    request.UserId, order.Id, lpn.Id, request.SkuId, before,
                    new { request.SkuId, request.Quantity, request.ExpirationDate },
                    CommandType, request.RequestId);
                if (!audit.IsSuccess)
                    return audit.Error!;
                return new CommandExecutionResult(lpn.Id, [audit.Value!]);
            }, ct);
    }
}

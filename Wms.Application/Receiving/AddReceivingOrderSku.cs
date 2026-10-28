using Wms.Application.Authorization;
using Wms.Application.CommandExecution;
using Wms.Common;
using Wms.Domain;

namespace Wms.Application.Receiving;

public sealed record AddReceivingOrderSkuRequest(
    Guid RequestId, Guid OrderId, Guid SkuId, string UserId);

public sealed class AddReceivingOrderSku(
    CommandExecutor executor,
    IReceivingSkuStore store,
    WarehousePermission permission,
    TimeProvider clock)
{
    private const string CommandType = "receiving.order.add-sku";

    public Task<OperationResult<Guid>> ExecuteAsync(
        AddReceivingOrderSkuRequest request, CancellationToken ct = default)
    {
        if (request is null || request.OrderId == Guid.Empty || request.SkuId == Guid.Empty)
            return Task.FromResult<OperationResult<Guid>>(
                OperationError.Invalid("Ордер и SKU обязательны."));

        var hash = CommandExecutor.ComputeHash($"{request.OrderId:N}:{request.SkuId:N}");
        return executor.ExecuteAsync(CommandType, request.RequestId, hash, request.UserId,
            async (session, operationCt) =>
            {
                var allowed = await permission.RequireOperatorAsync(
                    request.UserId, operationCt);
                if (!allowed.IsSuccess)
                    return allowed.Error!;

                var state = await store.LoadAddStateAsync(
                    request.OrderId, request.SkuId, operationCt);
                if (!state.OrderExists)
                    return OperationError.NotFound("Приходный ордер не найден.");
                if (!state.SkuExists)
                    return OperationError.NotFound("SKU не найден в справочнике WMS.");
                if (state.SkuDeleted)
                    return OperationError.Conflict("SKU помечен удалённым и недоступен для выбора.");

                if (!state.IsInOrder)
                {
                    var item = ReceivingOrderItem.CreateForSku(
                        request.OrderId, request.SkuId, 0m);
                    if (!item.IsSuccess)
                        return item.Error!;
                    session.Stage(item.Value!);
                }

                var audit = WarehouseAuditEvent.Create(
                    state.IsInOrder ? "receiving.order.sku-already-present" : "receiving.order.sku-added",
                    clock.GetUtcNow(), request.UserId, null, request.OrderId, null,
                    request.SkuId, null, null,
                    state.IsInOrder ? "{\"alreadyPresent\":true}"
                        : "{\"planQuantity\":0,\"factQuantity\":null}",
                    commandUserId: request.UserId,
                    commandType: CommandType,
                    requestId: request.RequestId);
                if (!audit.IsSuccess)
                    return audit.Error!;
                return new CommandExecutionResult(request.SkuId, [audit.Value!]);
            }, ct);
    }
}

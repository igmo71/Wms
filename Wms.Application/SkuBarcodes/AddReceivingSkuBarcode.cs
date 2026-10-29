using System.Text.Json;
using Wms.Application.Authorization;
using Wms.Application.CommandExecution;
using Wms.Common;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Application.SkuBarcodes;

public sealed record AddReceivingSkuBarcodeRequest(
    Guid RequestId, Guid OrderId, Guid SkuId, string Barcode, string UserId);

public sealed record SkuBarcodeAssignment(Guid SkuId, string? SkuName, string? SkuCode);

public interface IReceivingSkuBarcodeStore
{
    Task<ReceivingOrder?> LoadOrderAsync(ICommandExecutionContext session,
        Guid orderId, CancellationToken ct);
    Task<bool> IsCurrentSkuAsync(ICommandExecutionContext session,
        Guid orderId, Guid skuId, CancellationToken ct);
    Task<IReadOnlyList<SkuBarcodeAssignment>> FindAssignmentsAsync(
        ICommandExecutionContext session, string value, CancellationToken ct);
    void GuardOrderState(ICommandExecutionContext session, ReceivingOrder order);
}

public sealed class AddReceivingSkuBarcode(
    CommandExecutor executor, IReceivingSkuBarcodeStore store,
    ISkuBarcodeMutationLock mutationLock, WarehousePermission permission,
    TimeProvider clock)
{
    private const string CommandType = "receiving.sku.barcode.add";

    public async Task<OperationResult<Guid>> ExecuteAsync(
        AddReceivingSkuBarcodeRequest request, CancellationToken ct = default)
    {
        if (request is null || request.OrderId == Guid.Empty || request.SkuId == Guid.Empty)
            return OperationError.Invalid("Ордер и SKU обязательны.");
        var value = request.Barcode?.Trim();
        if (string.IsNullOrEmpty(value) || value.Length > 32)
            return OperationError.Invalid("Штрихкод должен содержать от 1 до 32 символов.");

        var hash = CommandExecutor.ComputeHash($"{request.OrderId:N}:{request.SkuId:N}:{value}");
        // The import, manual additions, and removals share this lock through commit.
        await using var lease = await mutationLock.AcquireAsync(ct);
        return await executor.ExecuteAsync(CommandType, request.RequestId, hash,
            request.UserId, async (session, operationCt) =>
            {
                var access = await permission.RequireOperatorAsync(request.UserId, operationCt);
                if (!access.IsSuccess)
                    return access.Error!;

                var order = await store.LoadOrderAsync(session, request.OrderId, operationCt);
                if (order is null)
                    return OperationError.NotFound("Приходный ордер не найден.");
                if (order.Status != ReceivingOrderStatus.InReceiving)
                    return OperationError.Conflict("Ордер не находится в активном цикле приёмки.");
                var cycle = order.Cycles.SingleOrDefault(x =>
                    x.Number == order.ActiveCycleNumber && x.StartedAtUtc != null
                    && x.CompletedAtUtc == null);
                if (cycle is null || !order.Participants.Any(x =>
                        x.ReceivingCycleId == cycle.Id && x.UserId == request.UserId?.Trim()))
                    return OperationError.Conflict("Сначала присоединитесь к текущему циклу приёмки.");
                if (!order.Items.Any(x => x.StockKeepingUnitId == request.SkuId)
                    || !await store.IsCurrentSkuAsync(session, order.Id, request.SkuId, operationCt))
                    return OperationError.NotFound("SKU отсутствует в текущем составе ордера.");

                var assignments = await store.FindAssignmentsAsync(session, value, operationCt);
                var other = assignments.FirstOrDefault(x => x.SkuId != request.SkuId);
                if (other is not null)
                {
                    var skuName = !string.IsNullOrWhiteSpace(other.SkuName)
                        ? other.SkuName.Trim()
                        : !string.IsNullOrWhiteSpace(other.SkuCode)
                            ? $"код {other.SkuCode.Trim()}" : "SKU без наименования";
                    return OperationError.Conflict(
                        $"Штрихкод уже назначен другому SKU: {skuName}.");
                }

                var alreadyPresent = assignments.Any(x => x.SkuId == request.SkuId);
                if (!alreadyPresent)
                    session.Stage(new SkuBarcode
                    {
                        SkuId = request.SkuId, Value = value, Source = SkuBarcodeSource.Wms
                    });
                store.GuardOrderState(session, order);

                var audit = WarehouseAuditEvent.Create(
                    alreadyPresent ? "receiving.sku.barcode.already-present"
                        : "receiving.sku.barcode.added",
                    clock.GetUtcNow(), request.UserId, null, order.Id, null,
                    request.SkuId, null, null,
                    JsonSerializer.Serialize(new { Value = value, AlreadyPresent = alreadyPresent }),
                    commandUserId: request.UserId,
                    commandType: CommandType,
                    requestId: request.RequestId);
                if (!audit.IsSuccess)
                    return audit.Error!;
                return new CommandExecutionResult(request.SkuId, [audit.Value!]);
            }, ct);
    }
}

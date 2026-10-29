using System.Text.Json;
using Wms.Application.Authorization;
using Wms.Application.CommandExecution;
using Wms.Common;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Application.SkuBarcodes;

public sealed record RemoveLocalSkuBarcodeRequest(
    Guid RequestId, Guid SkuId, string Value, string UserId);

public interface ISkuBarcodeRemovalStore
{
    Task<SkuBarcode?> LoadForRemovalAsync(ICommandExecutionContext session,
        Guid skuId, string value, CancellationToken ct);
    void StageRemoval(ICommandExecutionContext session, SkuBarcode barcode);
}

public sealed class RemoveLocalSkuBarcode(
    CommandExecutor executor, ISkuBarcodeRemovalStore store,
    ISkuBarcodeMutationLock mutationLock, WarehousePermission permission,
    TimeProvider clock)
{
    public const string RemovedEventType = "sku.barcode.removed";
    private const string CommandType = "sku.barcode.remove-local";

    public async Task<OperationResult<Guid>> ExecuteAsync(
        RemoveLocalSkuBarcodeRequest request, CancellationToken ct = default)
    {
        if (request is null || request.SkuId == Guid.Empty)
            return OperationError.Invalid("Выберите SKU и штрихкод.");
        var value = request.Value?.Trim();
        if (string.IsNullOrEmpty(value) || value.Length > 32)
            return OperationError.Invalid("Штрихкод должен содержать от 1 до 32 символов.");

        var hash = CommandExecutor.ComputeHash($"{request.SkuId:N}:{value}");
        await using var lease = await mutationLock.AcquireAsync(ct);
        return await executor.ExecuteAsync(CommandType, request.RequestId, hash,
            request.UserId, async (session, operationCt) =>
            {
                var access = await permission.RequireManagerAsync(request.UserId, operationCt);
                if (!access.IsSuccess)
                    return access.Error!;

                var barcode = await store.LoadForRemovalAsync(session,
                    request.SkuId, value, operationCt);
                if (barcode is null)
                    return OperationError.NotFound("Штрихкод уже удалён или изменён. Обновите список.");
                if (barcode.Source != SkuBarcodeSource.Wms)
                    return OperationError.Conflict(
                        "Штрихкод принадлежит 1С. Обновите список и исправьте запись в 1С.");

                store.StageRemoval(session, barcode);
                var audit = WarehouseAuditEvent.Create(RemovedEventType,
                    clock.GetUtcNow(), request.UserId, null,
                    null, null, barcode.SkuId, null,
                    JsonSerializer.Serialize(new { barcode.Value, Source = barcode.Source.ToString() }),
                    null,
                    commandUserId: request.UserId,
                    commandType: CommandType,
                    requestId: request.RequestId);
                if (!audit.IsSuccess)
                    return audit.Error!;
                return new CommandExecutionResult(barcode.SkuId, [audit.Value!]);
            }, ct);
    }
}

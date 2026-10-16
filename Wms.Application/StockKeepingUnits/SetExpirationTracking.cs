using System.Text.Json;
using Wms.Application.Authorization;
using Wms.Application.CommandExecution;
using Wms.Common;
using Wms.Domain;

namespace Wms.Application.StockKeepingUnits;

public sealed record SetExpirationTrackingRequest(
    Guid RequestId, Guid SkuId, bool Enabled, string UserId);

public sealed record ExpirationTrackingChange(
    DateTimeOffset OccurredAtUtc, string ActorName, bool Before, bool After);

public interface IExpirationTrackingStore
{
    Task<StockKeepingUnit?> LoadForChangeAsync(
        ICommandExecutionContext session, Guid skuId, CancellationToken ct);

    Task<IReadOnlyList<ExpirationTrackingChange>> ListHistoryAsync(
        Guid skuId, CancellationToken ct);
}

public sealed class SetExpirationTracking(
    CommandExecutor executor, IExpirationTrackingStore store,
    WarehousePermission permission, TimeProvider clock)
{
    public const string ChangedEventType = "sku.expiration-tracking.changed";
    private const string CommandType = "sku.expiration-tracking.set";

    public Task<OperationResult<Guid>> ExecuteAsync(
        SetExpirationTrackingRequest request, CancellationToken ct = default)
    {
        if (request is null || request.SkuId == Guid.Empty)
            return Task.FromResult<OperationResult<Guid>>(
                OperationError.Invalid("Выберите номенклатуру."));

        var hash = CommandExecutor.ComputeHash($"{request.SkuId:N}:{request.Enabled}");
        return executor.ExecuteAsync(CommandType, request.RequestId, hash, request.UserId,
            async (session, operationCt) =>
            {
                var access = await permission.RequireManagerAsync(request.UserId, operationCt);
                if (!access.IsSuccess)
                    return access.Error!;
                var sku = await store.LoadForChangeAsync(session, request.SkuId, operationCt);
                if (sku is null)
                    return OperationError.NotFound("Номенклатура не найдена.");

                var before = sku.TracksExpirationDate;
                sku.SetExpirationTracking(request.Enabled);
                var audit = WarehouseAuditEvent.Create(
                    before == request.Enabled
                        ? "sku.expiration-tracking.unchanged" : ChangedEventType,
                    clock.GetUtcNow(), request.UserId, null,
                    null, null, sku.Id, null,
                    JsonSerializer.Serialize(new { TracksExpirationDate = before }),
                    JsonSerializer.Serialize(new { TracksExpirationDate = request.Enabled }),
                    commandUserId: request.UserId,
                    commandType: CommandType,
                    requestId: request.RequestId);
                if (!audit.IsSuccess)
                    return audit.Error!;
                return new CommandExecutionResult(sku.Id, [audit.Value!]);
            }, ct);
    }

    public Task<IReadOnlyList<ExpirationTrackingChange>> ListHistoryAsync(
        Guid skuId, CancellationToken ct = default) => store.ListHistoryAsync(skuId, ct);
}

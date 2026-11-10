using System.Text.Json;
using Wms.Application.Authorization;
using Wms.Application.CommandExecution;
using Wms.Common;
using Wms.Domain;

namespace Wms.Application.Picking;

public sealed record DetachPickingSourceRequest(
    Guid RequestId, Guid PickingAssemblyId, Guid SourceRefKey, string UserId);

public sealed class DetachPickingSource(
    CommandExecutor executor, IPickingAssemblyStore store,
    WarehousePermission permission, TimeProvider clock)
{
    private const string CommandType = "picking.assembly.source.detach";

    public async Task<OperationResult<Guid>> ExecuteAsync(
        DetachPickingSourceRequest request, CancellationToken ct = default)
    {
        if (request is null || request.PickingAssemblyId == Guid.Empty
            || request.SourceRefKey == Guid.Empty || request.RequestId == Guid.Empty
            || string.IsNullOrWhiteSpace(request.UserId) || request.UserId.Length > 36)
            return OperationError.Invalid("Для открепления нужны сборка, РО 1С и исполнитель.");

        var hash = CommandExecutor.ComputeHash(
            $"{request.PickingAssemblyId:N}:{request.SourceRefKey:N}");
        return await executor.ExecuteAsync(CommandType, request.RequestId, hash,
            request.UserId, async (session, operationCt) =>
            {
                var access = await permission.RequireOperatorAsync(request.UserId, operationCt);
                if (!access.IsSuccess)
                    return access.Error!;
                var state = await store.LoadAsync(session, request.PickingAssemblyId,
                    request.SourceRefKey, operationCt);
                if (state.Assembly is null)
                    return OperationError.NotFound("Сборка WMS не найдена.");
                if (state.Assembly.ResponsibleUserId != request.UserId.Trim())
                    return OperationError.Conflict("Сначала примите сборку на себя.");
                if (state.LinkedAssemblyId != request.PickingAssemblyId)
                    return OperationError.Conflict("РО 1С не привязан к этой сборке.");
                var before = state.Assembly.ActiveSources.Count;
                var now = clock.GetUtcNow();
                var detached = state.Assembly.DetachSource(
                    request.SourceRefKey, request.UserId, now);
                if (!detached.IsSuccess)
                    return detached.Error!;

                var audit = WarehouseAuditEvent.Create(
                    "picking.assembly.source-detached", now, request.UserId, null,
                    null, null, null, null,
                    JsonSerializer.Serialize(new { request.SourceRefKey, Before = before }),
                    JsonSerializer.Serialize(new { After = state.Assembly.ActiveSources.Count,
                        state.Assembly.Status }),
                    commandUserId: request.UserId, commandType: CommandType,
                    requestId: request.RequestId,
                    pickingAssemblyId: request.PickingAssemblyId);
                if (!audit.IsSuccess)
                    return audit.Error!;
                return new CommandExecutionResult(request.PickingAssemblyId, [audit.Value!]);
            }, ct);
    }
}

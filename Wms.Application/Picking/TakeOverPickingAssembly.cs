using System.Text.Json;
using Wms.Application.Authorization;
using Wms.Application.CommandExecution;
using Wms.Common;
using Wms.Domain;

namespace Wms.Application.Picking;

public sealed record TakeOverPickingAssemblyRequest(
    Guid RequestId, Guid PickingAssemblyId, long ExpectedRevision, string UserId);

public sealed class TakeOverPickingAssembly(
    CommandExecutor executor, IPickingAssemblyStore store,
    WarehousePermission permission, TimeProvider clock)
{
    private const string CommandType = "picking.assembly.take-over";

    public Task<OperationResult<Guid>> ExecuteAsync(
        TakeOverPickingAssemblyRequest request, CancellationToken ct = default)
    {
        if (request is null || request.RequestId == Guid.Empty
            || request.PickingAssemblyId == Guid.Empty || request.ExpectedRevision < 0
            || string.IsNullOrWhiteSpace(request.UserId) || request.UserId.Length > 36)
            return Task.FromResult<OperationResult<Guid>>(
                OperationError.Invalid("Укажите сборку, ревизию и сотрудника."));

        var hash = CommandExecutor.ComputeHash(
            $"{request.PickingAssemblyId:N}:{request.ExpectedRevision}");
        return executor.ExecuteAsync(CommandType, request.RequestId, hash,
            request.UserId, async (session, operationCt) =>
            {
                var access = await permission.RequireOperatorAsync(request.UserId, operationCt);
                if (!access.IsSuccess)
                    return access.Error!;
                var assembly = await store.LoadAssemblyAsync(session,
                    request.PickingAssemblyId, operationCt);
                if (assembly is null)
                    return OperationError.NotFound("Сборка WMS не найдена.");

                var previousUserId = assembly.ResponsibleUserId;
                var now = clock.GetUtcNow();
                var changed = assembly.TakeOver(request.UserId, request.ExpectedRevision, now);
                if (!changed.IsSuccess)
                    return changed.Error!;
                var audit = WarehouseAuditEvent.Create(
                    "picking.assembly.taken-over", now, request.UserId, null,
                    null, null, null, null,
                    JsonSerializer.Serialize(new { ResponsibleUserId = previousUserId,
                        Revision = request.ExpectedRevision }),
                    JsonSerializer.Serialize(new { assembly.ResponsibleUserId,
                        Revision = assembly.OperationalRevision }),
                    commandUserId: request.UserId, commandType: CommandType,
                    requestId: request.RequestId,
                    pickingAssemblyId: request.PickingAssemblyId);
                if (!audit.IsSuccess)
                    return audit.Error!;
                return new CommandExecutionResult(assembly.Id, [audit.Value!]);
            }, ct);
    }
}

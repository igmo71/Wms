using System.Text.Json;
using Wms.Application.Authorization;
using Wms.Application.CommandExecution;
using Wms.Common;
using Wms.Domain;

namespace Wms.Application.Picking;

public sealed record AttachPickingSourceRequest(
    Guid RequestId, Guid PickingAssemblyId, Guid SourceRefKey, string UserId);

public sealed class AttachPickingSource(
    CommandExecutor executor, IPickingAssemblyStore store,
    SynchronizeShippingSource synchronize, WarehousePermission permission,
    TimeProvider clock)
{
    private const string CommandType = "picking.assembly.source.attach";

    public async Task<OperationResult<Guid>> ExecuteAsync(
        AttachPickingSourceRequest request, CancellationToken ct = default)
    {
        if (request is null || request.PickingAssemblyId == Guid.Empty
            || request.SourceRefKey == Guid.Empty || request.RequestId == Guid.Empty
            || string.IsNullOrWhiteSpace(request.UserId) || request.UserId.Length > 36)
            return OperationError.Invalid("Для привязки нужны сборка, РО 1С и исполнитель.");
        var access = await permission.RequireOperatorAsync(request.UserId, ct);
        if (!access.IsSuccess)
            return access.Error!;

        var hash = CommandExecutor.ComputeHash(
            $"{request.PickingAssemblyId:N}:{request.SourceRefKey:N}");
        var linkedBefore = await store.FindLinkedAssemblyIdAsync(request.SourceRefKey, ct);
        var refreshed = linkedBefore is null
            ? await synchronize.ExecuteAsync(request.SourceRefKey, ct)
            : OperationResult<Guid>.Success(request.SourceRefKey);

        return await executor.ExecuteAsync(CommandType, request.RequestId, hash,
            request.UserId, async (session, operationCt) =>
            {
                var state = await store.LoadAsync(session, request.PickingAssemblyId,
                    request.SourceRefKey, operationCt);
                if (state.Assembly is null)
                    return OperationError.NotFound("Сборка WMS не найдена.");
                if (state.Assembly.ResponsibleUserId != request.UserId.Trim())
                    return OperationError.Conflict("Сначала примите сборку на себя.");
                if (state.LinkedAssemblyId is Guid linkedId)
                    return OperationError.Conflict(linkedId == request.PickingAssemblyId
                        ? "РО 1С уже в этой сборке."
                        : "РО 1С уже привязан к другой сборке.");
                if (!refreshed.IsSuccess)
                    return refreshed.Error!;
                if (state.Source is null)
                    return OperationError.NotFound("РО 1С не найден после сверки.");

                var plan = PickingSourceEligibility.Plan(state.Source,
                    state.Assembly.WarehouseId);
                if (!plan.IsSuccess)
                    return plan.Error!;
                var now = clock.GetUtcNow();
                var attached = state.Assembly.AttachSource(request.SourceRefKey,
                    state.Source.CurrentSourceJson!, plan.Value!, request.UserId, now);
                if (!attached.IsSuccess)
                    return attached.Error!;
                session.Stage(attached.Value!);
                foreach (var line in state.Assembly.PlanLines.Where(x =>
                    x.ShippingOrderId == request.SourceRefKey))
                    session.Stage(line);

                var audit = WarehouseAuditEvent.Create(
                    "picking.assembly.source-attached", now, request.UserId, null,
                    null, null, null, null,
                    JsonSerializer.Serialize(new { request.PickingAssemblyId,
                        Before = state.Assembly.ActiveSources.Count - 1 }),
                    JsonSerializer.Serialize(new { request.SourceRefKey,
                        After = state.Assembly.ActiveSources.Count }),
                    commandUserId: request.UserId, commandType: CommandType,
                    requestId: request.RequestId,
                    pickingAssemblyId: request.PickingAssemblyId);
                if (!audit.IsSuccess)
                    return audit.Error!;
                return new CommandExecutionResult(request.PickingAssemblyId, [audit.Value!]);
            }, ct);
    }
}

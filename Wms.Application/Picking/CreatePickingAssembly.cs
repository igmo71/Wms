using System.Text.Json;
using Wms.Application.Authorization;
using Wms.Application.CommandExecution;
using Wms.Common;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Application.Picking;

public sealed record CreatePickingAssemblyRequest(
    Guid RequestId, Guid SourceRefKey, Guid WarehouseId, string UserId);

public sealed class CreatePickingAssembly(
    CommandExecutor executor, IPickingAssemblyStore store,
    SynchronizeShippingSource synchronize, WarehousePermission permission,
    TimeProvider clock)
{
    private const string CommandType = "picking.assembly.create-or-open";

    public async Task<OperationResult<Guid>> ExecuteAsync(
        CreatePickingAssemblyRequest request, CancellationToken ct = default)
    {
        if (request is null || request.RequestId == Guid.Empty
            || request.SourceRefKey == Guid.Empty || request.WarehouseId == Guid.Empty
            || string.IsNullOrWhiteSpace(request.UserId) || request.UserId.Length > 36)
            return OperationError.Invalid("Укажите РО 1С для открытия сборки.");
        var access = await permission.RequireOperatorAsync(request.UserId, ct);
        if (!access.IsSuccess)
            return access.Error!;
        var hash = CommandExecutor.ComputeHash(
            $"{request.SourceRefKey:N}:{request.WarehouseId:N}");
        var linkedBefore = await store.FindLinkedAssemblyIdAsync(request.SourceRefKey, ct);
        var refreshed = linkedBefore is null
            ? await synchronize.ExecuteAsync(request.SourceRefKey, ct)
            : OperationResult<Guid>.Success(request.SourceRefKey);

        return await executor.ExecuteAsync(CommandType, request.RequestId, hash,
            request.UserId, async (session, operationCt) =>
            {
                var access = await permission.RequireOperatorAsync(request.UserId, operationCt);
                if (!access.IsSuccess)
                    return access.Error!;
                var state = await store.LoadAsync(session, null, request.SourceRefKey, operationCt);
                if (state.LinkedAssemblyId is Guid linkedId)
                {
                    if (state.Assembly?.WarehouseId != request.WarehouseId)
                        return OperationError.Conflict("Документ относится к другому складу.");
                    if (state.Assembly?.Status is PickingAssemblyStatus.Cancelled
                        or PickingAssemblyStatus.CancellationPending)
                        return OperationError.Conflict("Привязка документа ожидает завершения отмены сборки.");
                    var opened = Audit("picking.assembly.opened", linkedId,
                        request, clock.GetUtcNow(), null);
                    if (!opened.IsSuccess)
                        return opened.Error!;
                    return new CommandExecutionResult(linkedId, [opened.Value!]);
                }
                if (!refreshed.IsSuccess)
                    return refreshed.Error!;
                if (state.Source is null)
                    return OperationError.NotFound("РО 1С не найден после сверки.");
                if (state.Source.WarehouseId != request.WarehouseId)
                    return OperationError.Conflict("Документ относится к другому складу.");
                var plan = PickingSourceEligibility.Plan(state.Source);
                if (!plan.IsSuccess)
                    return plan.Error!;

                var now = clock.GetUtcNow();
                var created = PickingAssembly.CreateNew(state.Source.WarehouseId,
                    request.UserId, now);
                if (!created.IsSuccess)
                    return created.Error!;
                var assembly = created.Value!;
                var attached = assembly.AttachSource(request.SourceRefKey,
                    state.Source.CurrentSourceJson!, plan.Value!, request.UserId, now);
                if (!attached.IsSuccess)
                    return attached.Error!;
                var numbered = await store.AssignNumberAsync(session, assembly, operationCt);
                if (!numbered.IsSuccess)
                    return numbered.Error!;

                session.Stage(assembly);
                var audit = Audit("picking.assembly.created", assembly.Id,
                    request, now, assembly.Number);
                if (!audit.IsSuccess)
                    return audit.Error!;
                return new CommandExecutionResult(assembly.Id, [audit.Value!]);
            }, ct);
    }

    private static OperationResult<WarehouseAuditEvent> Audit(
        string eventType, Guid assemblyId, CreatePickingAssemblyRequest request,
        DateTimeOffset now, string? number) =>
        WarehouseAuditEvent.Create(eventType, now, request.UserId, null,
            null, null, null, null, null,
            JsonSerializer.Serialize(new { request.SourceRefKey, Number = number }),
            commandUserId: request.UserId, commandType: CommandType,
            requestId: request.RequestId, pickingAssemblyId: assemblyId);
}

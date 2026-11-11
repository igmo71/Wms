using System.Text.Json;
using Wms.Application.Authorization;
using Wms.Application.CommandExecution;
using Wms.Common;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Application.Picking;

public sealed record CancelPickingAssemblyRequest(
    Guid RequestId, Guid PickingAssemblyId, long ExpectedRevision,
    string UserId, string? Reason = null);

public sealed class CancelPickingAssembly(
    CommandExecutor executor, IPickingAssemblyStore assemblies,
    WarehousePermission permission, TimeProvider clock)
{
    private const string CommandType = "picking.assembly.cancel";

    public Task<OperationResult<Guid>> ExecuteAsync(
        CancelPickingAssemblyRequest request, CancellationToken ct = default)
    {
        if (request is null || request.RequestId == Guid.Empty
            || request.PickingAssemblyId == Guid.Empty || request.ExpectedRevision < 0
            || string.IsNullOrWhiteSpace(request.UserId) || request.UserId.Length > 36
            || request.Reason?.Trim().Length > 500)
            return Task.FromResult<OperationResult<Guid>>(
                OperationError.Invalid("Укажите сборку, ревизию и руководителя для отмены."));
        var hash = CommandExecutor.ComputeHash(
            $"{request.PickingAssemblyId:N}:{request.ExpectedRevision}:"
            + request.Reason?.Trim());
        return executor.ExecuteAsync(CommandType, request.RequestId, hash,
            request.UserId, async (session, token) =>
            {
                var access = await permission.RequireManagerAsync(request.UserId, token);
                if (!access.IsSuccess) return access.Error!;
                var state = await assemblies.LoadCancellationAsync(session,
                    request.PickingAssemblyId, token);
                var assembly = state.Assembly;
                if (assembly is null)
                    return OperationError.NotFound("Сборка WMS не найдена.");
                if (state.Entries.Any(x => x.Quantity > 0)
                    || state.ActiveReservations.Count > 0)
                    return OperationError.Conflict(
                        "Перед отменой верните весь отобранный товар в исходные ячейки.");
                if (state.PalletHolds.Count > 0)
                    return OperationError.Conflict(
                        "Перед отменой верните снятые поддоны и подтвердите исходные ячейки.");

                var now = clock.GetUtcNow();
                var previousStatus = assembly.Status;
                var cancelledTasks = 0;
                var activeTasks = 0;
                foreach (var task in state.Replenishments)
                {
                    if (task.Status == ReplenishmentTaskStatus.Ordered)
                    {
                        var cancelled = task.Cancel(request.UserId, now);
                        if (!cancelled.IsSuccess) return cancelled.Error!;
                        cancelledTasks++;
                    }
                    else if (task.Status == ReplenishmentTaskStatus.InProgress)
                        activeTasks++;
                }
                var requested = assembly.RequestCancellation(request.UserId,
                    request.ExpectedRevision, now);
                if (!requested.IsSuccess) return requested.Error!;
                if (activeTasks == 0 && assembly.ActiveSources.Count == 0)
                {
                    var completed = assembly.CompleteCancellation(now,
                        hasActiveReplenishments: false);
                    if (!completed.IsSuccess) return completed.Error!;
                }
                var audit = WarehouseAuditEvent.Create("picking.assembly.cancellation-requested",
                    now, request.UserId, null, null, null, null, null,
                    JsonSerializer.Serialize(new { Status = previousStatus,
                        Revision = request.ExpectedRevision }),
                    JsonSerializer.Serialize(new { assembly.Status,
                        assembly.OperationalRevision, CancelledReplenishments = cancelledTasks,
                        ActiveReplenishments = activeTasks,
                        PendingSources = assembly.ActiveSources.Count,
                        Reason = request.Reason?.Trim() }),
                    commandUserId: request.UserId, commandType: CommandType,
                    requestId: request.RequestId, pickingAssemblyId: assembly.Id);
                if (!audit.IsSuccess) return audit.Error!;
                return new CommandExecutionResult(assembly.Id, [audit.Value!]);
            }, ct);
    }
}

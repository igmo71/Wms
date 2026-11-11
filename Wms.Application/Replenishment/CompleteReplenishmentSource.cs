using System.Text.Json;
using Wms.Application.Authorization;
using Wms.Application.CommandExecution;
using Wms.Common;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Application.Replenishment;

public sealed record CompleteReplenishmentSourceRequest(
    Guid RequestId, Guid ReplenishmentTaskId, Guid SourceCycleId,
    string ScannedSourceLocationCode, bool SourceWasEmpty, string UserId);

public sealed class CompleteReplenishmentSource(
    CommandExecutor executor, IReplenishmentStore replenishments,
    WarehousePermission permission, TimeProvider clock)
{
    private const string CommandType = "replenishment.source.complete";

    public Task<OperationResult<Guid>> ExecuteAsync(
        CompleteReplenishmentSourceRequest request, CancellationToken ct = default)
    {
        if (request is null || request.RequestId == Guid.Empty
            || request.ReplenishmentTaskId == Guid.Empty || request.SourceCycleId == Guid.Empty
            || string.IsNullOrWhiteSpace(request.UserId) || request.UserId.Length > 36)
            return Task.FromResult<OperationResult<Guid>>(
                OperationError.Invalid("Укажите задание, поддон, скан источника и подпитчика."));
        var normalized = StorageLocationAddress.Normalize(request.ScannedSourceLocationCode);
        if (!normalized.IsSuccess)
            return Task.FromResult<OperationResult<Guid>>(normalized.Error!);
        var code = normalized.Value!;
        var hash = CommandExecutor.ComputeHash(
            $"{request.ReplenishmentTaskId:N}:{request.SourceCycleId:N}:"
            + $"{code}:{request.SourceWasEmpty}");
        return executor.ExecuteAsync(CommandType, request.RequestId, hash,
            request.UserId, async (session, token) =>
            {
                var access = await permission.RequireOperatorAsync(request.UserId, token);
                if (!access.IsSuccess) return access.Error!;
                var state = await replenishments.LoadActiveSourceAsync(session,
                    request.ReplenishmentTaskId, token);
                var task = state.Task;
                if (task is null)
                    return OperationError.NotFound("Задание Подпитки не найдено.");
                if (task.Status != ReplenishmentTaskStatus.InProgress
                    || task.AssignedTo != request.UserId.Trim())
                    return OperationError.Conflict("Задание выполняется другим подпитчиком.");
                var cycle = task.SourceCycles.SingleOrDefault(x =>
                    x.Id == request.SourceCycleId && x.CompletedAtUtc is null);
                if (cycle is null)
                    return OperationError.Conflict("Снятый поддон уже завершён или не принадлежит заданию.");
                var source = state.Source;
                if (source is null || source.Id != cycle.SourceStorageLocationId
                    || source.Code != code || source.WarehouseId != task.WarehouseId
                    || source.ActiveLock?.OwnerType != StorageLocationLockOwnerType.ReplenishmentTask
                    || source.ActiveLock.OwnerId != task.Id)
                    return OperationError.Conflict(
                        "Отсканируйте исходную ячейку, удерживаемую этим заданием.");
                if (request.SourceWasEmpty && await replenishments.HasUnblockedStockAsync(
                        session, task.WarehouseId, source.Id, token))
                    return OperationError.Conflict(
                        "В исходной ячейке остался товар. Подтвердите возврат поддона.");

                var now = clock.GetUtcNow();
                var completed = task.CompleteSource(cycle.Id, source.Id,
                    request.SourceWasEmpty, request.UserId, now);
                if (!completed.IsSuccess) return completed.Error!;
                var released = await replenishments.ReleaseSourceHoldAsync(session,
                    task.WarehouseId, source.Id, task.Id, token);
                if (!released.IsSuccess) return released.Error!;

                var assembly = await replenishments.LoadCancellationAssemblyAsync(
                    session, task.PickingAssemblyId, token);
                if (assembly?.Status == PickingAssemblyStatus.CancellationPending
                    && assembly.ActiveSources.Count == 0)
                {
                    var related = await replenishments.LoadForAssemblyAsync(session,
                        assembly.Id, token);
                    if (related.All(x => x.Status != ReplenishmentTaskStatus.InProgress))
                    {
                        var cancelled = assembly.CompleteCancellation(now,
                            hasActiveReplenishments: false);
                        if (!cancelled.IsSuccess) return cancelled.Error!;
                    }
                }
                var audit = WarehouseAuditEvent.Create(
                    "replenishment.source.completed", now, request.UserId,
                    null, null, null, task.StockKeepingUnitId, null, null,
                    JsonSerializer.Serialize(new { TaskId = task.Id,
                        CycleId = cycle.Id, SourceLocationId = source.Id,
                        request.SourceWasEmpty, task.TransferredQuantity,
                        task.RemainingQuantity, task.Status,
                        AssemblyCancellationCompleted = assembly?.Status
                            == PickingAssemblyStatus.Cancelled }),
                    commandUserId: request.UserId, commandType: CommandType,
                    requestId: request.RequestId,
                    pickingAssemblyId: task.PickingAssemblyId);
                if (!audit.IsSuccess) return audit.Error!;
                return new CommandExecutionResult(cycle.Id, [audit.Value!]);
            }, ct);
    }
}

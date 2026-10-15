using Wms.Application.Authorization;
using Wms.Application.CommandExecution;
using Wms.Common;
using Wms.Domain.Enums;

namespace Wms.Application.Receiving;

public sealed record JoinReceivingCycleRequest(
    Guid RequestId, Guid OrderId, Guid? ReceivingLocationId, string UserId);

public sealed class JoinReceivingCycle(
    CommandExecutor executor, IReceivingWorkStore store,
    SynchronizeReceivingSource synchronize, IReceivingSourceParser parser,
    WarehousePermission permission, TimeProvider clock)
{
    private const string CommandType = "receiving.cycle.join";

    public Task<OperationResult<Guid>> ExecuteAsync(
        JoinReceivingCycleRequest request, CancellationToken ct = default)
    {
        if (request is null || request.OrderId == Guid.Empty)
            return Task.FromResult<OperationResult<Guid>>(
                OperationError.Invalid("Идентификатор ордера обязателен."));

        var hash = CommandExecutor.ComputeHash(
            $"{request.OrderId:N}:{request.ReceivingLocationId:N}");
        return executor.ExecuteAsync(CommandType, request.RequestId, hash, request.UserId,
            async (session, operationCt) =>
            {
                var access = await permission.RequireReceivingWorkerAsync(
                    request.UserId, operationCt);
                if (!access.IsSuccess)
                    return access.Error!;

                var refKeys = await store.LoadSourceRefKeysAsync(request.OrderId, operationCt);
                if (refKeys.Count == 0)
                    return OperationError.Conflict("У ордера нет действующих источников 1С.");
                foreach (var refKey in refKeys)
                {
                    var refreshed = await synchronize.ExecuteAsync(refKey, operationCt);
                    if (!refreshed.IsSuccess)
                        return OperationError.Conflict(
                            $"Свежая сверка источника {refKey} не удалась: {refreshed.Error!.Message}");
                }

                var order = await store.LoadOrderAsync(session, request.OrderId, operationCt);
                if (order is null)
                    return OperationError.NotFound("Приходный ордер не найден.");
                var sources = await store.LoadSourcesAsync(session, order.Id, operationCt);
                if (sources.Count != refKeys.Count)
                    return OperationError.Conflict("Состав источников изменился. Повторите вступление.");
                foreach (var source in sources)
                {
                    if (source.VersionId is null || source.SnapshotJson is null
                        || source.LastAttemptError is not null)
                        return OperationError.Conflict("Источник 1С не подтверждён свежей сверкой.");
                    var parsed = parser.Parse(source.RefKey, source.SnapshotJson);
                    if (!parsed.IsSuccess || parsed.Value!.Posted != true
                        || parsed.Value.DeletionMark != false
                        || parsed.Value.WarehouseId != order.WarehouseId)
                        return OperationError.Conflict(
                            $"Источник {source.RefKey} не допускает вступления в приёмку.");
                }

                if (order.Status == ReceivingOrderStatus.ReadyForReceiving)
                {
                    if (request.ReceivingLocationId is not Guid locationId)
                        return OperationError.Invalid("Первый участник должен выбрать место приёмки.");
                    var location = await store.LoadLocationAsync(session, locationId, operationCt);
                    if (location is null || location.WarehouseId != order.WarehouseId
                        || location.DeletionMark || location.IsFolder
                        || location.Zone?.Type != ZoneType.Receiving
                        || location.ActiveLock is not null)
                        return OperationError.Conflict("Место приёмки недоступно или принадлежит другому складу.");
                }

                var now = clock.GetUtcNow();
                var joined = order.JoinCurrentCycle(
                    request.ReceivingLocationId, request.UserId, now);
                if (!joined.IsSuccess)
                    return joined.Error!;
                var outcome = joined.Value!;
                var audit = ReceivingWorkAudit.Create(
                    outcome.Joined ? "receiving.cycle.joined" : "receiving.cycle.already-joined",
                    now, request.UserId, order.Id, null, null, null,
                    new { outcome.CycleId, outcome.ReceivingLocationId,
                        outcome.Started, outcome.Joined }, CommandType, request.RequestId);
                if (!audit.IsSuccess)
                    return audit.Error!;
                return new CommandExecutionResult(order.Id, [audit.Value!]);
            }, ct);
    }
}

using Wms.Application.Authorization;
using Wms.Application.CommandExecution;
using Wms.Common;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Application.Receiving;

public enum ReceivingSourceChangeKind { Attach, Detach }
public enum ReceivingSourceChangeChannel { Web, Mobile }

public sealed record ChangeReceivingOrderSourcesRequest(
    Guid RequestId, Guid OrderId, Guid SourceRefKey, string UserId,
    ReceivingSourceChangeKind Kind, ReceivingSourceChangeChannel Channel,
    string? StateToken = null);

public sealed record ReceivingSourceChangeState(
    ReceivingOrder? Order, ReceivingSourceState? Target,
    ReceivingOrderSourceLink? TargetLink, Guid? TargetLinkedOrderId,
    IReadOnlyList<ReceivingSourceState> ActiveSources,
    bool HasPostedLpn)
{
    public IReadOnlyList<ReceivingOrderSourceLink> ActiveLinks { get; init; } = [];
}

public interface IReceivingOrderSourceChangeStore
{
    Task<ReceivingSourceChangeState> LoadPreviewAsync(
        Guid orderId, Guid sourceRefKey, CancellationToken ct);
    Task<ReceivingSourceChangeState> LoadAsync(
        ICommandExecutionContext session, Guid orderId, Guid sourceRefKey, CancellationToken ct);
    void GuardSources(ICommandExecutionContext session,
        IReadOnlyList<ReceivingSourceState> sources);
}

public sealed class ChangeReceivingOrderSources(
    CommandExecutor executor, IReceivingOrderSourceChangeStore store,
    IReceivingWorkQueryStore orders,
    IReceivingSourceParser parser, SynchronizeReceivingSource synchronize,
    WarehousePermission permission, TimeProvider clock)
{
    private const string CommandType = "receiving.order.change-sources";

    public Task<OperationResult<Guid>> ExecuteAsync(
        ChangeReceivingOrderSourcesRequest request, CancellationToken ct = default)
    {
        if (request is null || request.OrderId == Guid.Empty || request.SourceRefKey == Guid.Empty
            || !Enum.IsDefined(request.Kind) || !Enum.IsDefined(request.Channel))
            return Task.FromResult<OperationResult<Guid>>(
                OperationError.Invalid("Ордер, источник и действие обязательны."));

        var hash = CommandExecutor.ComputeHash(
            $"{request.OrderId:N}:{request.SourceRefKey:N}:{request.Kind}:{request.Channel}");
        return executor.ExecuteAsync(CommandType, request.RequestId, hash, request.UserId,
            async (session, operationCt) =>
            {
                var access = await permission.RequireReceivingWorkerAsync(request.UserId, operationCt);
                if (!access.IsSuccess)
                    return access.Error!;
                var canManage = (await permission.RequireManagerAsync(
                    request.UserId, operationCt)).IsSuccess;
                if (request.Channel == ReceivingSourceChangeChannel.Web && !canManage)
                    return OperationError.Conflict("Для изменения источников в Web нужна роль Manager.");
                if (request.Channel == ReceivingSourceChangeChannel.Web
                    && string.IsNullOrWhiteSpace(request.StateToken))
                    return OperationError.Invalid("Перед изменением состава обновите предпросмотр.");

                if (request.Kind == ReceivingSourceChangeKind.Attach
                    && request.Channel == ReceivingSourceChangeChannel.Mobile)
                {
                    var status = await orders.GetOrderStatusAsync(
                        request.OrderId, operationCt);
                    if (status is null)
                        return OperationError.NotFound("Приходный ордер не найден.");
                    if (status is not (ReceivingOrderStatus.ReadyForReceiving
                        or ReceivingOrderStatus.InReceiving or ReceivingOrderStatus.Received))
                        return OperationError.Conflict("Состав источников нельзя изменить в текущем статусе ордера.");
                    if (status == ReceivingOrderStatus.Received
                        && (!canManage || request.Channel != ReceivingSourceChangeChannel.Web))
                        return OperationError.Conflict("Для изменения источников нужен Manager в Web.");
                    var linkedOrderId = await orders.FindLinkedOrderIdBySourceAsync(
                        request.SourceRefKey, operationCt);
                    if (linkedOrderId is Guid id)
                        return OperationError.Conflict(id == request.OrderId
                            ? "Источник уже привязан к этому ордеру."
                            : "Источник уже привязан к другому ордеру.");
                    var refreshed = await synchronize.ExecuteAsync(request.SourceRefKey, operationCt);
                    if (!refreshed.IsSuccess)
                        return refreshed.Error!;
                }

                var state = await store.LoadAsync(
                    session, request.OrderId, request.SourceRefKey, operationCt);
                if (state.Order is null)
                    return OperationError.NotFound("Приходный ордер не найден.");
                var order = state.Order;
                if (order.Status is not (ReceivingOrderStatus.ReadyForReceiving
                    or ReceivingOrderStatus.InReceiving or ReceivingOrderStatus.Received))
                    return OperationError.Conflict("Состав источников нельзя изменить в текущем статусе ордера.");
                if (request.Channel == ReceivingSourceChangeChannel.Web
                    && !string.Equals(request.StateToken,
                        ReceivingSourceCompositionToken.Create(
                            state, request.Kind, request.SourceRefKey),
                        StringComparison.Ordinal))
                    return OperationError.Conflict("Данные изменились, обновите предпросмотр.");

                var managerOnly = order.Status == ReceivingOrderStatus.Received
                    || request.Kind == ReceivingSourceChangeKind.Detach && state.HasPostedLpn;
                if (managerOnly && (!canManage || request.Channel != ReceivingSourceChangeChannel.Web))
                    return OperationError.Conflict("Для изменения источников нужен Manager в Web.");

                var target = state.Target;
                if (target is null || target.Version is null
                    || target.Source.LastSuccessfulCheckAtUtc is null)
                    return OperationError.NotFound("Источник не найден или ещё не сверен с 1С.");

                var alreadyAttached = state.TargetLink is not null;
                if (request.Kind == ReceivingSourceChangeKind.Attach)
                {
                    if (state.TargetLinkedOrderId is Guid otherId && otherId != order.Id)
                        return OperationError.Conflict("Источник уже привязан к другому ордеру.");
                    if (alreadyAttached)
                        return OperationError.Conflict("Источник уже привязан к этому ордеру.");
                }
                else if (!alreadyAttached)
                    return OperationError.Conflict("Источник не привязан к этому ордеру.");

                var targetParsed = parser.Parse(request.SourceRefKey,
                    target.Version.SnapshotJson);
                if (!targetParsed.IsSuccess)
                    return targetParsed.Error!;
                if (request.Kind == ReceivingSourceChangeKind.Attach)
                {
                    if (targetParsed.Value!.WarehouseId is not Guid warehouseId)
                        return OperationError.Conflict("Склад источника не подтверждён.");
                    if (warehouseId != order.WarehouseId)
                        return OperationError.Conflict("Склад источника отличается от склада ордера.");
                    var sender = ReceivingSourceSenderPolicy.CheckAttachment(
                        state, targetParsed.Value, parser);
                    if (!sender.IsSuccess)
                        return sender.Error!;
                }

                var included = request.Kind == ReceivingSourceChangeKind.Attach
                    ? state.ActiveSources.Append(target).ToArray()
                    : state.ActiveSources.Where(x => x.Source.Id != target.Source.Id).ToArray();
                var contributions = new List<(ParsedReceivingSource Source, ReceivingSourcePlan Plan)>();
                foreach (var source in included)
                {
                    if (source.Version is null)
                        return OperationError.Conflict("Версия связанного источника не найдена.");
                    var parsed = source.Source.Id == target.Source.Id
                        ? targetParsed : parser.Parse(source.Source.RefKey, source.Version.SnapshotJson);
                    if (!parsed.IsSuccess)
                        return parsed.Error!;
                    contributions.Add((parsed.Value!, source.Version.CalculatePlan()));
                }

                var plan = ReceivingOrderPlanProjection.Sum(order.WarehouseId, contributions);
                var updated = order.ApplyCurrentSourcePlan(plan);
                if (!updated.IsSuccess)
                    return updated.Error!;
                foreach (var item in updated.Value!)
                    session.Stage(item);

                var now = clock.GetUtcNow();
                if (request.Kind == ReceivingSourceChangeKind.Attach)
                {
                    var sender = ReceivingSourceSenderPolicy.GetId(targetParsed.Value!);
                    var assigned = order.SetSender(sender.Value);
                    if (!assigned.IsSuccess)
                        return assigned.Error!;
                    var link = ReceivingOrderSourceLink.Create(
                        order.Id, target.Source.Id, now);
                    if (!link.IsSuccess)
                        return link.Error!;
                    session.Stage(link.Value!);
                }
                else
                {
                    var detached = state.TargetLink!.Detach(now);
                    if (!detached.IsSuccess)
                        return detached.Error!;
                    if (included.Length == 0 && !state.HasPostedLpn)
                    {
                        var cleared = order.ClearSender();
                        if (!cleared.IsSuccess)
                            return cleared.Error!;
                    }
                }

                order.RecordSourceCompositionChange();
                var guardedSources = request.Channel == ReceivingSourceChangeChannel.Web
                    && request.Kind == ReceivingSourceChangeKind.Detach
                    ? included.Append(target).ToArray() : included;
                store.GuardSources(session, guardedSources);
                var audit = WarehouseAuditEvent.Create(
                    request.Kind == ReceivingSourceChangeKind.Attach
                        ? "receiving.order.source-attached" : "receiving.order.source-detached",
                    now, request.UserId, null, order.Id, null, null, target.Source.Id,
                    $"{{\"sourceCount\":{state.ActiveSources.Count}}}",
                    $"{{\"sourceCount\":{included.Length}}}",
                    sourceVersionIds: [target.Version.Id],
                    commandUserId: request.UserId, commandType: CommandType,
                    requestId: request.RequestId);
                if (!audit.IsSuccess)
                    return audit.Error!;
                return new CommandExecutionResult(order.Id, [audit.Value!]);
            }, ct);
    }
}

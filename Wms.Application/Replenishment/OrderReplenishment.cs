using System.Globalization;
using System.Text.Json;
using Wms.Application.Authorization;
using Wms.Application.CommandExecution;
using Wms.Application.Inventory;
using Wms.Application.Picking;
using Wms.Common;
using Wms.Domain;

namespace Wms.Application.Replenishment;

public sealed record OrderReplenishmentRequest(
    Guid RequestId, Guid PickingAssemblyId, long ExpectedRevision,
    Guid StockKeepingUnitId, Guid SourceLocationId,
    Guid? ExpectedDestinationLocationId, decimal Quantity, string UserId);

public sealed class OrderReplenishment(
    CommandExecutor executor, IPickingAssemblyStore assemblies,
    IReplenishmentStore replenishments, IStockAvailabilityStore stock,
    BuildPickingRoute routeBuilder, BuildReplenishmentProposals proposalBuilder,
    WarehousePermission permission, TimeProvider clock)
{
    private const string CommandType = "replenishment.order";

    public Task<OperationResult<Guid>> ExecuteAsync(OrderReplenishmentRequest request,
        CancellationToken ct = default)
    {
        if (request is null || request.RequestId == Guid.Empty
            || request.PickingAssemblyId == Guid.Empty || request.ExpectedRevision < 0
            || request.StockKeepingUnitId == Guid.Empty || request.SourceLocationId == Guid.Empty
            || request.ExpectedDestinationLocationId == Guid.Empty
            || !WarehouseQuantity.IsPositive(request.Quantity)
            || string.IsNullOrWhiteSpace(request.UserId) || request.UserId.Length > 36)
            return Task.FromResult<OperationResult<Guid>>(
                OperationError.Invalid("Укажите сборку, предложение Подпитки и сотрудника."));

        var hash = CommandExecutor.ComputeHash(
            $"{request.PickingAssemblyId:N}:{request.ExpectedRevision}:" +
            $"{request.StockKeepingUnitId:N}:{request.SourceLocationId:N}:" +
            $"{request.ExpectedDestinationLocationId:N}:" +
            request.Quantity.ToString(CultureInfo.InvariantCulture));
        return executor.ExecuteAsync(CommandType, request.RequestId, hash,
            request.UserId, async (session, token) =>
            {
                var access = await permission.RequireOperatorAsync(request.UserId, token);
                if (!access.IsSuccess) return access.Error!;
                var work = await assemblies.LoadWorkAsync(session,
                    request.PickingAssemblyId, null, token);
                var assembly = work.Assembly;
                if (assembly is null)
                    return OperationError.NotFound("Сборка WMS не найдена.");
                if (assembly.OperationalRevision != request.ExpectedRevision)
                    return OperationError.Conflict("Сборка изменилась. Обновите предложения Подпитки.");

                var remaining = assembly.PlanLines.GroupBy(x => x.StockKeepingUnitId)
                    .ToDictionary(x => x.Key, x => x.Sum(line => line.Quantity)
                        - work.Entries.Where(entry => entry.StockKeepingUnitId == x.Key)
                            .Sum(entry => entry.Quantity));
                remaining = remaining.Where(x => x.Value > 0)
                    .ToDictionary(x => x.Key, x => x.Value);
                if (!remaining.ContainsKey(request.StockKeepingUnitId))
                    return OperationError.Conflict("Этот SKU больше не требует Подпитки.");

                var availability = new List<StockAvailability>();
                foreach (var skuId in remaining.Keys)
                    availability.AddRange(await stock.ReadSkuAsync(session,
                        assembly.WarehouseId, skuId, token));
                var state = await replenishments.LoadProposalStateAsync(session,
                    assembly.WarehouseId, remaining.Keys.ToArray(), token);
                var routeLocations = state.Locations.Select(x =>
                    new PickingRouteLocation(x.Id, x.Code, x.ZoneType,
                        x.IsActive, x.CurrentSkuIds.Count)).ToArray();
                var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
                var route = routeBuilder.Execute(new PickingRouteRequest(assembly.Id,
                    assembly.WarehouseId, today, remaining, availability,
                    routeLocations));
                if (!route.IsSuccess) return route.Error!;
                var calculated = proposalBuilder.Execute(new ReplenishmentProposalRequest(
                    assembly.Id, assembly.WarehouseId, today, remaining,
                    route.Value!.Steps, availability, state.Locations,
                    state.ActiveTasks));
                if (!calculated.IsSuccess) return calculated.Error!;
                var proposal = calculated.Value!.Proposals.SingleOrDefault(x =>
                    x.StockKeepingUnitId == request.StockKeepingUnitId
                    && x.SourceLocationId == request.SourceLocationId
                    && x.DestinationLocationId == request.ExpectedDestinationLocationId
                    && x.Quantity == request.Quantity);
                if (proposal is null)
                    return OperationError.Conflict(
                        "Предложение Подпитки изменилось. Обновите список перед заказом.");

                var changed = assembly.RecordReplenishmentOrder(request.UserId,
                    request.ExpectedRevision);
                if (!changed.IsSuccess) return changed.Error!;
                var now = clock.GetUtcNow();
                var task = ReplenishmentTask.Order(Guid.NewGuid(), assembly.Id,
                    assembly.WarehouseId, request.StockKeepingUnitId,
                    proposal.Quantity, proposal.SourceLocationId,
                    proposal.DestinationLocationId, request.UserId, now);
                if (!task.IsSuccess) return task.Error!;
                session.Stage(task.Value!);
                var audit = WarehouseAuditEvent.Create("replenishment.ordered", now,
                    request.UserId, null, null, null, request.StockKeepingUnitId,
                    null, null,
                    JsonSerializer.Serialize(new { TaskId = task.Value!.Id,
                        SourceLocationId = proposal.SourceLocationId,
                        DestinationLocationId = proposal.DestinationLocationId,
                        proposal.Quantity, Revision = assembly.OperationalRevision }),
                    commandUserId: request.UserId, commandType: CommandType,
                    requestId: request.RequestId, pickingAssemblyId: assembly.Id);
                if (!audit.IsSuccess) return audit.Error!;
                return new CommandExecutionResult(task.Value!.Id, [audit.Value!]);
            }, ct);
    }
}

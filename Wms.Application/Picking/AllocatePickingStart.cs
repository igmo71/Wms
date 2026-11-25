using System.Text.Json;
using Wms.Application.CommandExecution;
using Wms.Application.Inventory;
using Wms.Common;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Application.Picking;

public sealed class AllocatePickingStart(
    CommandExecutor executor, IPickingAssemblyStore assemblies, IStockAvailabilityStore stock,
    BuildPickingRoute routeBuilder, TimeProvider clock)
{
    private const string CommandType = "picking.assembly.start.allocate";

    public async Task<OperationResult> ExecuteAsync(StartPickingAssemblyRequest request,
        Guid warehouseId, CancellationToken ct)
    {
        var hash = CommandExecutor.ComputeHash($"{request.PickingAssemblyId:N}:{request.RequestId:N}");
        while (true)
        {
            var commandId = Guid.NewGuid();
            var allocated = await executor.ExecuteAsync(CommandType, commandId, hash, request.UserId,
                (session, token) => AllocateNextAsync(session, request, warehouseId, commandId, token), ct);
            if (!allocated.IsSuccess)
            {
                // A concurrent start can have issued this assembly while this request was waiting.
                var current = await assemblies.ReadAssemblyAsync(request.PickingAssemblyId, ct);
                return current is { Status: PickingAssemblyStatus.InProgress }
                    && current.ResponsibleUserId == request.UserId.Trim()
                    ? OperationResult.Success() : allocated.Error!;
            }
            if (allocated.Value == request.PickingAssemblyId)
                return OperationResult.Success();
        }
    }

    private async Task<OperationResult<CommandExecutionResult>> AllocateNextAsync(
        ICommandExecutionContext session, StartPickingAssemblyRequest request,
        Guid warehouseId, Guid commandId, CancellationToken ct)
    {
        var selected = await assemblies.LoadNextReadyStartAsync(
            session, warehouseId, request.PickingAssemblyId, ct);
        if (!selected.IsSuccess)
            return selected.Error!;
        var selection = selected.Value!;
        if (selection.Requested is not { IsReadyForAllocation: true } requested
            || requested.ResponsibleUserId != request.UserId.Trim()
            || selection.Next is not { IsReadyForAllocation: true } assembly)
            return OperationError.Conflict("Готовность сборки изменилась. Повторите начало сборки.");

        var remaining = assembly.PlanLines.GroupBy(x => x.StockKeepingUnitId)
            .ToDictionary(x => x.Key, x => x.Sum(line => line.Quantity));
        var skuIds = remaining.Where(x => x.Value > 0).Select(x => x.Key).ToArray();
        var locations = await assemblies.LockRouteLocationsAsync(session, warehouseId, skuIds, ct);
        var byId = locations.ToDictionary(x => x.Location.Id);
        var availability = await stock.ReadSkusAsync(session, warehouseId, skuIds, ct);
        if (availability.Any(x => x.AccountingQuantity > 0 && !byId.ContainsKey(x.Group.StorageLocationId)))
            return OperationError.Conflict("Состав ячеек с товаром изменился. Повторите начало сборки.");
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        var built = routeBuilder.Execute(new PickingRouteRequest(assembly.Id, warehouseId, today,
            remaining, availability, locations.Select(x => x.Route).ToArray()));
        if (!built.IsSuccess)
            return built.Error!;
        var route = built.Value!;

        foreach (var step in route.Steps)
        {
            var reservation = PickingRouteReservation.Create(assembly.Id, warehouseId,
                step.StorageLocationId, step.StockKeepingUnitId, step.ExpirationDate, step.Quantity);
            if (!reservation.IsSuccess)
                return reservation.Error!;
            session.Stage(reservation.Value!);
        }
        foreach (var locationId in route.Steps.Select(x => x.StorageLocationId).Distinct())
            byId[locationId].Location.AdvanceOperationalRevision();

        var now = clock.GetUtcNow();
        var started = assembly.CompleteStart(now);
        if (!started.IsSuccess)
            return started.Error!;
        var audit = WarehouseAuditEvent.Create("picking.assembly.start-stock-allocated", now,
            actorUserId: null, actionSource: "picking-start-queue", receivingOrderId: null,
            licensePlateNumberId: null, stockKeepingUnitId: null, receivingSourceDocumentId: null,
            beforeValuesJson: null, afterValuesJson: JsonSerializer.Serialize(new
            {
                ReservationCount = route.Steps.Count, StartRequestId = request.RequestId
            }),
            commandUserId: request.UserId, commandType: CommandType, requestId: commandId,
            pickingAssemblyId: assembly.Id);
        return audit.IsSuccess ? new CommandExecutionResult(assembly.Id, [audit.Value!]) : audit.Error!;
    }
}

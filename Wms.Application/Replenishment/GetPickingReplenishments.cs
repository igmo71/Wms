using Wms.Application.Inventory;
using Wms.Application.Picking;
using Wms.Common;
using Wms.Domain.Enums;

namespace Wms.Application.Replenishment;

public sealed record PickingReplenishmentProposalView(
    Guid StockKeepingUnitId, Guid SourceLocationId, string SourceCode,
    Guid? DestinationLocationId, string? DestinationCode,
    decimal Quantity, IReadOnlyList<ReplenishmentProposalGroup> Groups);

public sealed record PickingReplenishmentTaskView(
    Guid Id, Guid StockKeepingUnitId, decimal OrderedQuantity,
    decimal TransferredQuantity, ReplenishmentTaskStatus Status,
    Guid? RecommendedSourceLocationId, string? RecommendedSourceCode,
    Guid? RecommendedDestinationLocationId, string? RecommendedDestinationCode);

public sealed record PickingReplenishmentsView(
    Guid PickingAssemblyId, Guid WarehouseId, long Revision,
    IReadOnlyList<PickingReplenishmentProposalView> Proposals,
    IReadOnlyList<ReplenishmentDeficit> Deficits,
    IReadOnlyList<PickingReplenishmentTaskView> Tasks);

public sealed class GetPickingReplenishments(
    GetPickingWork workQuery, GetPickingRoute routeQuery,
    IStockAvailabilityStore stock, IReplenishmentStore store,
    BuildReplenishmentProposals builder, TimeProvider clock)
{
    public async Task<OperationResult<PickingReplenishmentsView>> ExecuteAsync(
        Guid assemblyId, string userId, CancellationToken ct = default)
    {
        var loaded = await workQuery.ExecuteAsync(assemblyId, userId, ct);
        if (!loaded.IsSuccess) return loaded.Error!;
        var work = loaded.Value!;
        if (work.Status != PickingAssemblyStatus.InProgress
            || work.ResponsibleUserId != userId.Trim())
            return OperationError.Conflict("Для заказа Подпитки примите начатую сборку на себя.");
        var route = await routeQuery.ExecuteAsync(assemblyId, userId, ct: ct);
        if (!route.IsSuccess) return route.Error!;
        if (route.Value!.Revision != work.Revision)
            return OperationError.Conflict("Сборка изменилась. Обновите предложения Подпитки.");
        var remaining = work.Skus.Where(x => x.PlannedQuantity > x.ConfirmedQuantity)
            .ToDictionary(x => x.SkuId,
                x => x.PlannedQuantity - x.ConfirmedQuantity);
        var availability = new List<StockAvailability>();
        foreach (var skuId in remaining.Keys)
            availability.AddRange(await stock.ReadSkuAsync(work.WarehouseId, skuId, ct));
        var state = await store.ReadProposalStateAsync(work.WarehouseId,
            remaining.Keys.ToArray(), ct);
        var proposals = builder.Execute(new ReplenishmentProposalRequest(
            assemblyId, work.WarehouseId,
            DateOnly.FromDateTime(clock.GetLocalNow().DateTime),
            remaining, route.Value.Steps, availability,
            state.Locations, state.ActiveTasks));
        if (!proposals.IsSuccess) return proposals.Error!;
        var locations = state.Locations.ToDictionary(x => x.Id);
        var tasks = await store.ReadAssemblyTasksAsync(assemblyId, ct);
        return new PickingReplenishmentsView(assemblyId, work.WarehouseId,
            work.Revision,
            proposals.Value!.Proposals.Select(x => new PickingReplenishmentProposalView(
                x.StockKeepingUnitId, x.SourceLocationId,
                locations[x.SourceLocationId].Code,
                x.DestinationLocationId,
                x.DestinationLocationId is Guid destinationId
                    && locations.TryGetValue(destinationId, out var destination)
                    ? destination.Code : null,
                x.Quantity, x.Groups)).ToArray(),
            proposals.Value.Deficits,
            tasks.Select(x => new PickingReplenishmentTaskView(x.Id,
                x.StockKeepingUnitId, x.OrderedQuantity, x.TransferredQuantity,
                x.Status, x.RecommendedSourceStorageLocationId,
                x.RecommendedSourceStorageLocationId is Guid sourceId
                    && locations.TryGetValue(sourceId, out var source)
                    ? source.Code : null,
                x.RecommendedDestinationStorageLocationId,
                x.RecommendedDestinationStorageLocationId is Guid targetId
                    && locations.TryGetValue(targetId, out var target)
                    ? target.Code : null)).ToArray());
    }
}

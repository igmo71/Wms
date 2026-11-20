using Wms.Application.Inventory;
using Wms.Application.Picking;
using Wms.Common;
using Wms.Domain.Enums;

namespace Wms.Application.Replenishment;

public sealed record PickingReplenishmentProposalView(
    Guid StockKeepingUnitId, Guid SourceLocationId, string SourceCode,
    Guid? DestinationLocationId, string? DestinationCode,
    decimal Quantity, IReadOnlyList<ReplenishmentProposalGroup> Groups)
{
    public string? SourceName { get; init; }
    public string? DestinationName { get; init; }
}

public sealed record PickingReplenishmentTaskView(
    Guid Id, Guid StockKeepingUnitId, decimal OrderedQuantity,
    decimal TransferredQuantity, ReplenishmentTaskStatus Status,
    Guid? RecommendedSourceLocationId, string? RecommendedSourceCode,
    Guid? RecommendedDestinationLocationId, string? RecommendedDestinationCode)
{
    public string? RecommendedSourceName { get; init; }
    public string? RecommendedDestinationName { get; init; }
}

public sealed record PickingReplenishmentsView(
    Guid PickingAssemblyId, Guid WarehouseId, long Revision,
    IReadOnlyList<PickingReplenishmentProposalView> Proposals,
    IReadOnlyList<ReplenishmentDeficit> Deficits,
    IReadOnlyList<PickingReplenishmentTaskView> Tasks,
    PickingWorkView Work, IReadOnlyDictionary<Guid, decimal> PickableBySku);

public sealed class GetPickingReplenishments(
    GetPickingWork workQuery, BuildPickingRoute routeBuilder,
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
        var remaining = work.Skus.Where(x => x.PlannedQuantity > x.ConfirmedQuantity)
            .ToDictionary(x => x.SkuId,
                x => x.PlannedQuantity - x.ConfirmedQuantity);
        var availability = await stock.ReadSkusAsync(work.WarehouseId,
            remaining.Keys.ToArray(), ct);
        var tasks = await store.ReadAssemblyTasksAsync(assemblyId, ct);
        var taskLocationIds = tasks.SelectMany(x => new[]
            { x.RecommendedSourceStorageLocationId, x.RecommendedDestinationStorageLocationId })
            .OfType<Guid>().Distinct().ToArray();
        var state = await store.ReadProposalStateAsync(work.WarehouseId,
            availability, taskLocationIds, ct);
        var routeLocationIds = availability.Select(x => x.Group.StorageLocationId).ToHashSet();
        var routeLocations = state.Locations.Where(x => routeLocationIds.Contains(x.Id)).Select(x =>
            new PickingRouteLocation(x.Id, x.Code, x.ZoneType,
                x.IsActive, x.DistinctSkuCount) { Name = x.Name }).ToArray();
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        var route = routeBuilder.Execute(new PickingRouteRequest(assemblyId,
            work.WarehouseId, today, remaining, availability, routeLocations));
        if (!route.IsSuccess) return route.Error!;
        var proposals = builder.Execute(new ReplenishmentProposalRequest(
            assemblyId, work.WarehouseId, today,
            remaining, route.Value!.Steps, availability,
            state.Locations, state.ActiveTasks));
        if (!proposals.IsSuccess) return proposals.Error!;
        var locations = state.Locations.ToDictionary(x => x.Id);
        return new PickingReplenishmentsView(assemblyId, work.WarehouseId,
            work.Revision,
            proposals.Value!.Proposals.Select(x => new PickingReplenishmentProposalView(
                x.StockKeepingUnitId, x.SourceLocationId,
                locations[x.SourceLocationId].Code,
                x.DestinationLocationId,
                x.DestinationLocationId is Guid destinationId
                    && locations.TryGetValue(destinationId, out var destination)
                    ? destination.Code : null,
                x.Quantity, x.Groups)
            {
                SourceName = locations[x.SourceLocationId].Name,
                DestinationName = x.DestinationLocationId is Guid destinationNameId
                    ? locations.GetValueOrDefault(destinationNameId)?.Name : null
            }).ToArray(),
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
                    ? target.Code : null)
            {
                RecommendedSourceName = x.RecommendedSourceStorageLocationId is Guid sourceNameId
                    ? locations.GetValueOrDefault(sourceNameId)?.Name : null,
                RecommendedDestinationName = x.RecommendedDestinationStorageLocationId is Guid targetNameId
                    ? locations.GetValueOrDefault(targetNameId)?.Name : null
            }).ToArray(), work,
            route.Value!.Steps.GroupBy(x => x.StockKeepingUnitId)
                .ToDictionary(x => x.Key, x => x.Sum(step => step.Quantity)));
    }
}

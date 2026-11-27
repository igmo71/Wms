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
    PickingWorkView Work, IReadOnlyDictionary<Guid, decimal> PickableBySku,
    IReadOnlyDictionary<Guid, PickingSkuStock> StockBySku);

public sealed record PickingSkuStock(PickingFreeStock Storage, PickingFreeStock Reserve,
    decimal FreeStorageQuantity);

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
            work.Skus.Select(x => x.SkuId).ToArray(), ct);
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
        var route = routeBuilder.ExecuteReserved(new PickingRouteRequest(assemblyId,
            work.WarehouseId, today, remaining, availability, routeLocations));
        if (!route.IsSuccess) return route.Error!;
        var proposals = builder.Execute(new ReplenishmentProposalRequest(
            assemblyId, work.WarehouseId, today,
            remaining, route.Value!.Steps, availability,
            state.Locations, state.ActiveTasks));
        if (!proposals.IsSuccess) return proposals.Error!;
        var locations = state.Locations.ToDictionary(x => x.Id);
        var stepsBySku = route.Value!.Steps.ToLookup(x => x.StockKeepingUnitId);
        var expiredBySku = availability.Where(x => x.Group.ExpirationDate < today
                && x.RouteReservedFor(assemblyId) > 0)
            .ToLookup(x => x.Group.StockKeepingUnitId);
        var freeStorageBySku = availability.Where(x => (x.Group.ExpirationDate is null || x.Group.ExpirationDate >= today)
                && locations.GetValueOrDefault(x.Group.StorageLocationId) is { IsActive: true, ZoneType: ZoneType.Storage })
            .ToLookup(x => x.Group.StockKeepingUnitId, x => x.AvailableQuantity);
        var stockBySku = work.Skus.ToDictionary(x => x.SkuId, x =>
        {
            var steps = stepsBySku[x.SkuId].ToLookup(step => step.DirectPalletPick);
            var expiredZones = expiredBySku[x.SkuId]
                .Select(group => locations.GetValueOrDefault(group.Group.StorageLocationId)?.ZoneType)
                .ToHashSet();
            return new PickingSkuStock(
                new PickingFreeStock(steps[false].Sum(step => step.Quantity), expiredZones.Contains(ZoneType.Storage)),
                new PickingFreeStock(steps[true].Sum(step => step.Quantity), expiredZones.Contains(ZoneType.Reserve)),
                freeStorageBySku[x.SkuId].Sum());
        });
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
            stepsBySku.ToDictionary(x => x.Key, x => x.Sum(step => step.Quantity)), stockBySku);
    }
}

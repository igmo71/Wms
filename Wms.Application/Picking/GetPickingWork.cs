using Wms.Application.Authorization;
using Wms.Application.Inventory;
using Wms.Common;
using Wms.Domain.Enums;

namespace Wms.Application.Picking;

public sealed class GetPickingWork(
    IPickingAssemblyQueryStore store, WarehousePermission permission)
{
    public async Task<OperationResult<PickingWorkView>> ExecuteAsync(
        Guid assemblyId, string userId, CancellationToken ct = default)
    {
        if (assemblyId == Guid.Empty || string.IsNullOrWhiteSpace(userId))
            return OperationError.Invalid("Укажите сборку и сотрудника.");
        var access = await permission.RequireOperatorAsync(userId, ct);
        if (!access.IsSuccess) return access.Error!;
        var work = await store.GetWorkAsync(assemblyId, ct);
        return work is null
            ? OperationError.NotFound("Сборка WMS не найдена.")
            : work;
    }
}

public sealed record PickingRouteView(long Revision,
    IReadOnlyList<PickingRouteStep> Steps,
    IReadOnlyList<PickingRouteGap> Gaps, PickingWorkView Work,
    IReadOnlyDictionary<(Guid LocationId, Guid SkuId), PickingFreeStock> StockByCell);

public sealed class GetPickingRoute(
    GetPickingWork workQuery, IPickingAssemblyQueryStore assemblies,
    IStockAvailabilityStore stock, BuildPickingRoute builder,
    TimeProvider clock)
{
    public async Task<OperationResult<PickingRouteView>> ExecuteAsync(
        Guid assemblyId, string userId, Guid? previousLocationId = null,
        Guid? nextLocationId = null, CancellationToken ct = default)
    {
        var loaded = await workQuery.ExecuteAsync(assemblyId, userId, ct);
        if (!loaded.IsSuccess) return loaded.Error!;
        var work = loaded.Value!;
        if (work.Status != PickingAssemblyStatus.InProgress
            || work.ResponsibleUserId != userId.Trim())
            return OperationError.Conflict("Для маршрута примите начатую сборку на себя.");
        var remaining = work.Skus.Where(x => x.PlannedQuantity > x.ConfirmedQuantity)
            .ToDictionary(x => x.SkuId,
                x => x.PlannedQuantity - x.ConfirmedQuantity);
        var availability = await stock.ReadSkusAsync(work.WarehouseId,
            remaining.Keys.ToArray(), ct);
        var locations = await assemblies.GetRouteLocationsAsync(work.WarehouseId,
            availability.Select(x => x.Group.StorageLocationId).Distinct().ToArray(), ct);
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        var route = builder.Execute(new PickingRouteRequest(assemblyId, work.WarehouseId, today,
            remaining, availability, locations, previousLocationId, nextLocationId));
        if (!route.IsSuccess) return route.Error!;

        var stockByCell = availability.GroupBy(x =>
                (x.Group.StorageLocationId, x.Group.StockKeepingUnitId))
            .ToDictionary(x => x.Key, x => PickingFreeStock.Sum(x.Select(group =>
                PickingFreeStock.ForGroup(group, assemblyId, today))));
        return new PickingRouteView(work.Revision, route.Value!.Steps, route.Value.Gaps, work,
            stockByCell);
    }
}

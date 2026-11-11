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
    IReadOnlyList<PickingRouteGap> Gaps);

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
        var availability = new List<StockAvailability>();
        foreach (var skuId in remaining.Keys)
            availability.AddRange(await stock.ReadSkuAsync(work.WarehouseId, skuId, ct));
        var locations = await assemblies.GetRouteLocationsAsync(work.WarehouseId,
            availability.Select(x => x.Group.StorageLocationId).Distinct().ToArray(), ct);
        var route = builder.Execute(new PickingRouteRequest(assemblyId,
            work.WarehouseId, DateOnly.FromDateTime(clock.GetLocalNow().DateTime),
            remaining, availability, locations, previousLocationId, nextLocationId));
        return route.IsSuccess
            ? new PickingRouteView(work.Revision, route.Value!.Steps, route.Value.Gaps)
            : route.Error!;
    }
}

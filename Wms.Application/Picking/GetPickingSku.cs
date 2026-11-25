using Wms.Application.Authorization;
using Wms.Application.Inventory;
using Wms.Common;
using Wms.Domain.Enums;

namespace Wms.Application.Picking;

public sealed record PickingSkuView(Guid PickingAssemblyId, Guid StockKeepingUnitId,
    decimal PlannedQuantity, decimal ConfirmedQuantity, decimal RemainingQuantity,
    IReadOnlyList<PickingSkuLocation> Locations);

public sealed record PickingSkuLocation(StockAvailability Stock, PickingFreeStock FreeStock,
    bool CanPickNow, bool FromReserve, bool OfferedForSelection);

public sealed class GetPickingSku(
    IPickingAssemblyStore assemblies, IStockAvailabilityStore stock,
    WarehousePermission permission, IPickingAssemblyQueryStore queries, TimeProvider clock)
{
    public async Task<OperationResult<PickingSkuView>> ExecuteAsync(
        Guid assemblyId, Guid skuId, string userId, CancellationToken ct = default)
    {
        if (assemblyId == Guid.Empty || skuId == Guid.Empty || string.IsNullOrWhiteSpace(userId))
            return OperationError.Invalid("Укажите сборку, SKU и сотрудника.");
        var access = await permission.RequireOperatorAsync(userId, ct);
        if (!access.IsSuccess) return access.Error!;
        var state = await assemblies.ReadWorkAsync(assemblyId, null, ct);
        var assembly = state.Assembly;
        if (assembly is null) return OperationError.NotFound("Сборка WMS не найдена.");
        if (assembly.Status != PickingAssemblyStatus.InProgress
            || assembly.ResponsibleUserId != userId.Trim())
            return OperationError.Conflict("Для Отбора примите начатую сборку на себя.");
        var planned = assembly.PlanLines.Where(x => x.StockKeepingUnitId == skuId)
            .Sum(x => x.Quantity);
        if (planned <= 0)
            return OperationError.Conflict("SKU не входит в текущий план сборки.");
        var confirmed = state.Entries.Where(x => x.StockKeepingUnitId == skuId)
            .Sum(x => x.Quantity);
        var remaining = Math.Max(0, planned - confirmed);
        var availability = await stock.ReadSkuAsync(assembly.WarehouseId, skuId, ct);
        var locations = await queries.GetRouteLocationsAsync(assembly.WarehouseId,
            availability.Where(x => x.AccountingQuantity > 0)
                .Select(x => x.Group.StorageLocationId).Distinct().ToArray(), ct);
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        var choices = GetLocations(assemblyId, remaining, today, availability,
                locations.ToDictionary(x => x.Id))
            .OrderBy(x => x.Stock.Group.ExpirationDate is null)
            .ThenBy(x => x.Stock.Group.ExpirationDate)
            .ThenByDescending(x => x.FreeStock.Quantity)
            .ThenByDescending(x => new PickingAddress(x.Stock.Group.StorageLocationId,
                x.Stock.LocationCode), PickingAddressComparer.Instance)
            .ToArray();
        return new PickingSkuView(assemblyId, skuId, planned, confirmed, remaining, choices);
    }

    internal static IEnumerable<PickingSkuLocation> GetLocations(Guid assemblyId,
        decimal remaining, DateOnly today, IReadOnlyList<StockAvailability> availability,
        IReadOnlyDictionary<Guid, PickingRouteLocation> locations)
    {
        var earliest = availability.Where(x => x.AvailableForAssemblyPick(assemblyId) > 0
                && x.Group.ExpirationDate >= today)
            .Min(x => x.Group.ExpirationDate);
        var earliestReserved = availability.Where(x => x.AvailableForRoute(assemblyId) > 0
                && x.Group.ExpirationDate >= today)
            .Min(x => x.Group.ExpirationDate);
        foreach (var group in availability.Where(x => x.AccountingQuantity > 0)
            .GroupBy(x => x.Group.StorageLocationId))
        {
            if (!locations.TryGetValue(group.Key, out var location) || !location.IsActive
                || location.ZoneType is not (ZoneType.Storage or ZoneType.Reserve)) continue;

            var cell = group.ToArray();
            var reserve = location.ZoneType == ZoneType.Reserve;
            var offeredPallet = false;
            if (reserve && remaining > 0 && location.DistinctSkuCount == 1)
            {
                var owned = cell.Any(x => x.Hold is { OwnerType: StorageLocationLockOwnerType.PickingAssembly }
                    && x.Hold.OwnerId == assemblyId);
                offeredPallet = owned || HoldPickingPallet.CheckWholePallet(cell, availability,
                    location.Id, assemblyId, remaining, today) is null;
            }

            foreach (var row in cell)
            {
                var free = PickingFreeStock.ForGroup(row, assemblyId, today);
                var offered = !reserve || (offeredPallet && free.Quantity > 0);
                var firstDate = row.AvailableForRoute(assemblyId) > 0 ? earliestReserved : earliest;
                var canPickNow = offered && remaining > 0 && free.Quantity > 0
                    && !free.HasExpiredStock && row.Group.ExpirationDate == firstDate;
                yield return new PickingSkuLocation(row, free, canPickNow, reserve, offered);
            }
        }
    }
}

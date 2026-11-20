using Wms.Application.Authorization;
using Wms.Application.Inventory;
using Wms.Common;
using Wms.Domain.Enums;

namespace Wms.Application.Picking;

public sealed record PickingSkuView(Guid PickingAssemblyId, Guid StockKeepingUnitId,
    decimal PlannedQuantity, decimal ConfirmedQuantity, decimal RemainingQuantity,
    IReadOnlyList<PickingSkuLocation> Locations);

public sealed record PickingSkuLocation(StockAvailability Stock, bool CanPickNow, bool FromReserve);

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
        var cells = availability.Where(x => x.AccountingQuantity > 0)
            .ToLookup(x => x.Group.StorageLocationId);
        var locations = await queries.GetRouteLocationsAsync(assembly.WarehouseId,
            cells.Select(x => x.Key).ToArray(), ct);
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        var eligible = availability.Where(x => x.AvailableForNewPick(assemblyId) > 0
            && (x.Group.ExpirationDate is null || x.Group.ExpirationDate >= today))
            .ToHashSet();
        var earliest = eligible.OrderBy(x => x.Group.ExpirationDate is null)
            .ThenBy(x => x.Group.ExpirationDate).FirstOrDefault()?.Group.ExpirationDate;
        var choices = locations.Where(x => x.IsActive
                && x.ZoneType is ZoneType.Storage or ZoneType.Reserve)
            .SelectMany(location =>
            {
                var cell = cells[location.Id].ToArray();
                var reserve = location.ZoneType == ZoneType.Reserve;
                var owned = cell.Any(x => x.Hold is { OwnerType: StorageLocationLockOwnerType.PickingAssembly }
                    && x.Hold.OwnerId == assemblyId);
                var wholePallet = reserve && location.DistinctSkuCount == 1
                    && (owned || HoldPickingPallet.CheckWholePallet(cell, availability,
                        location.Id, assemblyId, remaining, today) is null);
                return cell.Select(row => new
                {
                    Row = new PickingSkuLocation(row, remaining > 0 && eligible.Contains(row)
                        && row.Group.ExpirationDate == earliest && (!reserve || wholePallet), reserve),
                    Address = new PickingAddress(location.Id, location.Code)
                });
            })
            .OrderBy(x => x.Row.Stock.Group.ExpirationDate is null)
            .ThenBy(x => x.Row.Stock.Group.ExpirationDate)
            .ThenByDescending(x => x.Row.Stock.AvailableForNewPick(assemblyId))
            .ThenByDescending(x => x.Address, PickingAddressComparer.Instance)
            .Select(x => x.Row).ToArray();
        return new PickingSkuView(assemblyId, skuId, planned, confirmed, remaining, choices);
    }
}

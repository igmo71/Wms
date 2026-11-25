using Wms.Application.Authorization;
using Wms.Application.Inventory;
using Wms.Common;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Application.Picking;

public sealed record PickingSourceView(Guid PickingAssemblyId, Guid StorageLocationId,
    string LocationCode, Guid StockKeepingUnitId, DateOnly? ExpirationDate,
    decimal AccountingQuantity, decimal AvailableQuantity, decimal SuggestedQuantity,
    decimal ReservedQuantity, decimal BlockedQuantity, bool RequiresVisualExpiryCheck)
{
    public string? LocationName { get; init; }
    public bool FromReserve { get; init; }
    public bool PalletHeld { get; init; }
    public decimal ExpectedPhysicalQuantity { get; init; }
}

public sealed class GetPickingSource(
    IPickingAssemblyStore assemblies, IStockAvailabilityStore stock,
    WarehousePermission permission, TimeProvider clock)
{
    public async Task<OperationResult<PickingSourceView>> ExecuteAsync(
        Guid assemblyId, Guid skuId, string scannedLocationCode, string userId,
        Guid? scannedSkuId = null, DateOnly? selectedExpirationDate = null,
        bool dateGroupSelected = false, CancellationToken ct = default)
    {
        if (assemblyId == Guid.Empty || skuId == Guid.Empty
            || string.IsNullOrWhiteSpace(userId))
            return OperationError.Invalid("Укажите сборку, SKU и сотрудника.");
        var normalized = StorageLocationAddress.Normalize(scannedLocationCode);
        if (!normalized.IsSuccess) return normalized.Error!;
        var access = await permission.RequireOperatorAsync(userId, ct);
        if (!access.IsSuccess) return access.Error!;
        var state = await assemblies.ReadWorkAsync(assemblyId, normalized.Value, ct);
        var assembly = state.Assembly;
        if (assembly is null) return OperationError.NotFound("Сборка WMS не найдена.");
        if (assembly.Status != PickingAssemblyStatus.InProgress
            || assembly.ResponsibleUserId != userId.Trim())
            return OperationError.Conflict("Для Отбора примите начатую сборку на себя.");
        var availability = await stock.ReadSkuAsync(assembly.WarehouseId, skuId, ct);
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        var result = PickingSourceRules.Check(state, skuId, normalized.Value!, scannedSkuId,
            selectedExpirationDate, dateGroupSelected, availability, today);
        if (!result.IsSuccess || result.Value is not { FromReserve: true, PalletHeld: false } pallet)
            return result;
        var cell = availability.Where(x => x.Group.StorageLocationId == pallet.StorageLocationId
            && x.AccountingQuantity > 0).ToArray();
        var remaining = assembly.PlanLines.Where(x => x.StockKeepingUnitId == skuId).Sum(x => x.Quantity)
            - state.Entries.Where(x => x.StockKeepingUnitId == skuId).Sum(x => x.Quantity);
        var error = HoldPickingPallet.CheckWholePallet(cell, availability,
            pallet.StorageLocationId, assembly.Id, remaining, today);
        return error is null ? result : error;
    }
}

internal static class PickingSourceRules
{
    public static OperationResult<PickingSourceView> Check(
        PickingWorkState state, Guid skuId, string scannedCode, Guid? scannedSkuId,
        DateOnly? selectedExpirationDate, bool dateGroupSelected,
        IReadOnlyList<StockAvailability> availability, DateOnly today)
    {
        var assembly = state.Assembly!;
        var location = state.Location;
        if (location is null)
            return OperationError.NotFound($"Ячейка {scannedCode} не найдена на складе сборки.");
        if (scannedSkuId is Guid scanned && scanned != skuId)
            return OperationError.Conflict($"Сканированный SKU {scanned} не совпадает с выбранным {skuId}.");
        if (location.IsFolder || location.DeletionMark || location.Warehouse?.DeletionMark != false
            || location.Zone is null || location.Zone.DeletionMark
            || location.Zone.WarehouseId != assembly.WarehouseId
            || location.Zone.Type is not (ZoneType.Storage or ZoneType.Reserve))
            return OperationError.Conflict($"Ячейка {scannedCode} не подходит для Отбора.");
        if (!state.CellSkus.Any(x => x.Id == skuId))
        {
            var selected = assembly.PlanLines.FirstOrDefault(x =>
                x.StockKeepingUnitId == skuId)?.StockKeepingUnit;
            var selectedName = selected?.Name ?? selected?.Code ?? "выбранный SKU";
            var actual = state.CellSkus.Count == 0 ? "ячейка пуста"
                : string.Join(", ", state.CellSkus.Select(x =>
                    x.Name ?? x.Code ?? "SKU без наименования"));
            return OperationError.Conflict(
                $"В ячейке {scannedCode} нет SKU «{selectedName}»; числится: {actual}.");
        }
        var planned = assembly.PlanLines.Where(x => x.StockKeepingUnitId == skuId)
            .Sum(x => x.Quantity);
        var picked = state.Entries.Where(x => x.StockKeepingUnitId == skuId)
            .Sum(x => x.Quantity);
        var remaining = planned - picked;
        if (planned <= 0 || remaining <= 0)
            return OperationError.Conflict("Потребность в этом SKU уже выполнена или отсутствует.");
        if (dateGroupSelected && selectedExpirationDate is DateOnly requestedDate
            && requestedDate < today)
            return OperationError.Conflict("Срок годности выбранной группы истёк.");
        var fromReserve = location.Zone.Type == ZoneType.Reserve;
        var ownedHold = fromReserve
            && location.ActiveLock is { OwnerType: StorageLocationLockOwnerType.PickingAssembly }
            && location.ActiveLock.OwnerId == assembly.Id;
        if (fromReserve)
        {
            if (state.CellSkus.Count != 1)
                return OperationError.Conflict("Прямой Отбор из Хранения требует один SKU на поддоне.");
            if (!ownedHold && availability.Where(x => x.Group.StorageLocationId == location.Id)
                    .Sum(x => x.AccountingQuantity) > remaining)
                return OperationError.Conflict("Вся ячейка Хранения превышает потребность; нужна Подпитка.");
        }
        var eligible = availability.Where(x => x.Group.StockKeepingUnitId == skuId
            && (x.Group.ExpirationDate is null || x.Group.ExpirationDate >= today)
            && x.AvailableForAssemblyPick(assembly.Id) > 0)
            .OrderBy(x => x.Group.ExpirationDate is null)
            .ThenBy(x => x.Group.ExpirationDate).ToArray();
        if (eligible.Length == 0)
            return OperationError.Conflict("Свободного товара для Отбора нет.");
        var group = eligible.Where(x => x.Group.StorageLocationId == location.Id
                && (!dateGroupSelected || x.Group.ExpirationDate == selectedExpirationDate))
            .OrderByDescending(x => x.AvailableForRoute(assembly.Id) > 0)
            .FirstOrDefault();
        if (group is null)
            return OperationError.Conflict("В выбранной ячейке и группе срока нет доступного товара.");
        var assigned = group.AvailableForRoute(assembly.Id);
        var firstDate = eligible.First(x => assigned == 0 || x.AvailableForRoute(assembly.Id) > 0)
            .Group.ExpirationDate;
        if (group.Group.ExpirationDate != firstDate)
            return OperationError.Conflict("Сначала требуется отобрать товар с более ранним сроком годности.");
        var available = assigned > 0 ? assigned : group.AvailableForNewPick(assembly.Id);
        var remainingOutsideOtherPallets = remaining - HeldElsewhere(availability, assembly.Id, location.Id);
        if (remainingOutsideOtherPallets <= 0)
            return OperationError.Conflict("Оставшийся товар уже закреплён на удерживаемом поддоне.");

        return new PickingSourceView(assembly.Id, location.Id, scannedCode, skuId,
            group.Group.ExpirationDate, group.AccountingQuantity, available,
            Math.Min(remainingOutsideOtherPallets, available), group.ReservedQuantity,
            group.BlockedQuantity, group.Group.ExpirationDate is null
                && state.CellSkus.Single(x => x.Id == skuId).TracksExpirationDate)
        {
            LocationName = location.Name, FromReserve = fromReserve, PalletHeld = ownedHold,
            ExpectedPhysicalQuantity = Math.Max(0, group.AccountingQuantity - group.ConfirmedReservedQuantity)
        };
    }

    internal static decimal HeldElsewhere(IEnumerable<StockAvailability> availability, Guid assemblyId, Guid locationId) =>
        availability.Where(x => x.Group.StorageLocationId != locationId
                && x.Hold is { OwnerType: StorageLocationLockOwnerType.PickingAssembly } hold
                && hold.OwnerId == assemblyId)
            .Sum(x => x.RouteReservedFor(assemblyId));
}

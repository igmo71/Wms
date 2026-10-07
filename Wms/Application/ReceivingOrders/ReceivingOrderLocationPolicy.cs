using Microsoft.EntityFrameworkCore;
using Wms.Application.StorageLocations;
using Wms.Common;
using Wms.Data;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Application.ReceivingOrders;

internal static class ReceivingOrderLocationPolicy
{
    public static async Task<OperationResult> RequireReceivingLocationAsync(
        ApplicationDbContext dbContext,
        ReceivingOrder order,
        Guid? receivingLocationId,
        CancellationToken ct)
    {
        if (receivingLocationId is not Guid locationId)
        {
            return OperationError.Invalid("Для приёмки не указана позиция приёмки.");
        }

        var location = await dbContext.StorageLocations
            .Include(x => x.Warehouse)
            .Include(x => x.Zone)
            .Include(x => x.ActiveLock)
            .SingleOrDefaultAsync(x => x.Id == locationId, ct);

        if (location is null
            || !IsActiveLocation(location, order.WarehouseId, ZoneType.Receiving))
        {
            return OperationError.Invalid(
                "Позиция приёмки должна принадлежать зоне приёмки на складе ордера.");
        }

        return StorageLocationAvailability.ValidateUnlocked(location);
    }

    public static async Task<OperationResult> ValidatePutawayRoutesAsync(
        ApplicationDbContext dbContext,
        ReceivingOrder order,
        IReadOnlyCollection<InventoryMovement> movements,
        CancellationToken ct)
    {
        var locations = await LoadRouteLocationsAsync(dbContext, movements, ct);

        foreach (var movement in movements)
        {
            if (movement.SourceStorageLocationId is not Guid sourceId
                || movement.DestinationStorageLocationId is not Guid destinationId
                || !locations.TryGetValue(sourceId, out var source)
                || !locations.TryGetValue(destinationId, out var destination)
                || !IsActiveLocation(source, order.WarehouseId, ZoneType.Receiving)
                || !IsActiveLocation(destination, order.WarehouseId, ZoneType.Storage))
            {
                return OperationError.Invalid(
                    "Каждое движение размещения должно сохранять маршрут из активной позиции приёмки в активную позицию хранения склада ордера.");
            }
        }

        return OperationResult.Success();
    }

    private static async Task<Dictionary<Guid, StorageLocation>> LoadRouteLocationsAsync(
        ApplicationDbContext dbContext,
        IReadOnlyCollection<InventoryMovement> movements,
        CancellationToken ct)
    {
        var locationIds = movements
            .SelectMany(x => new[]
            {
                x.SourceStorageLocationId,
                x.DestinationStorageLocationId
            })
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .Distinct()
            .ToArray();

        return await dbContext.StorageLocations
            .Include(x => x.Warehouse)
            .Include(x => x.Zone)
            .Include(x => x.ActiveLock)
            .Where(x => locationIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);
    }

    private static bool IsActiveLocation(
        StorageLocation location,
        Guid warehouseId,
        ZoneType zoneType) =>
        location.WarehouseId == warehouseId
        && location.Warehouse is { DeletionMark: false }
        && location.Zone is { DeletionMark: false }
        && location.Zone.Type == zoneType
        && !location.IsFolder
        && !location.DeletionMark;
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wms.Common;
using Wms.Data;
using Wms.Domain;

namespace Wms.Application.Services.Inventory;

public class BalanceAndTurnoverService(ILogger<BalanceAndTurnoverService> logger)
{
    internal async Task<OperationResult> PostInventoryMovementsAsync(
        IReadOnlyCollection<InventoryMovement> movements,
        ApplicationDbContext dbContext,
        CancellationToken ct)
    {
        using var scope = logger.BeginScope("BalanceAndTurnover PostInventoryMovements {Count}", movements.Count);

        foreach (var movement in movements)
        {
            if (movement.Quantity <= 0)
            {
                logger.LogError("Inventory movement quantity must be greater than zero. Movement: {MovementId}", movement.Id);
                return OperationError.Invalid("Inventory movement quantity must be greater than zero.");
            }

            if (movement.SourceStorageLocationId is null && movement.DestinationStorageLocationId is null)
            {
                logger.LogError("Inventory movement source or destination must be specified. Movement: {MovementId}", movement.Id);
                return OperationError.Invalid("Inventory movement source or destination must be specified.");
            }

            if (movement.SourceStorageLocationId == movement.DestinationStorageLocationId)
            {
                logger.LogError("Inventory movement source and destination must be different. Movement: {MovementId}", movement.Id);
                return OperationError.Invalid("Inventory movement source and destination must be different.");
            }

            if (movement.PostedAtUtc is not null)
            {
                logger.LogError("Inventory movement has already been posted. Movement: {MovementId}", movement.Id);
                return OperationError.Failure("Inventory movement has already been posted.");
            }
        }

        var now = DateTimeOffset.UtcNow;
        var warehouseIds = movements.Select(x => x.WarehouseId).Distinct().ToArray();
        var storageLocationIds = movements
            .SelectMany(x => new[] { x.SourceStorageLocationId, x.DestinationStorageLocationId })
            .Where(x => x is not null)
            .Select(x => x!.Value)
            .Distinct()
            .ToArray();
        var stockKeepingUnitIds = movements.Select(x => x.StockKeepingUnitId).Distinct().ToArray();

        var locations = await dbContext.StorageLocations
            .Where(x => storageLocationIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);

        foreach (var movement in movements)
        {
            foreach (var locationId in new[] { movement.SourceStorageLocationId, movement.DestinationStorageLocationId }
                .Where(x => x.HasValue).Select(x => x!.Value))
            {
                if (!locations.TryGetValue(locationId, out var location)
                    || location.IsFolder
                    || location.WarehouseId != movement.WarehouseId)
                {
                    logger.LogError("Inventory movement location is invalid. Movement: {MovementId}, Location: {LocationId}",
                        movement.Id, locationId);
                    return OperationError.Invalid<StorageLocation>("Inventory movements require non-folder locations in their warehouse.");
                }
            }
        }

        var balances = await dbContext.InventoryBalances
            .Where(x => warehouseIds.Contains(x.WarehouseId)
                && storageLocationIds.Contains(x.StorageLocationId)
                && stockKeepingUnitIds.Contains(x.StockKeepingUnitId))
            .ToDictionaryAsync(x => (x.WarehouseId, x.StorageLocationId, x.StockKeepingUnitId), ct);

        foreach (var movement in movements)
        {
            if (movement.SourceStorageLocationId is not null)
            {
                var sourceKey = (movement.WarehouseId, movement.SourceStorageLocationId.Value, movement.StockKeepingUnitId);

                if (!balances.TryGetValue(sourceKey, out var sourceBalance))
                {
                    logger.LogError("Source inventory balance not found. Movement: {MovementId}", movement.Id);
                    return OperationError.Failure("Source inventory balance not found.");
                }

                if (sourceBalance.Quantity < movement.Quantity)
                {
                    logger.LogError("Insufficient source inventory balance. Movement: {MovementId}", movement.Id);
                    return OperationError.Failure("Insufficient source inventory balance.");
                }

                var balanceBefore = sourceBalance.Quantity;
                sourceBalance.Quantity -= movement.Quantity;
                sourceBalance.UpdatedAtUtc = now;

                dbContext.InventoryTurnovers.Add(new InventoryTurnover
                {
                    WarehouseId = movement.WarehouseId,
                    StorageLocationId = movement.SourceStorageLocationId.Value,
                    StockKeepingUnitId = movement.StockKeepingUnitId,
                    BalanceBefore = balanceBefore,
                    QuantityDelta = -movement.Quantity,
                    BalanceAfter = sourceBalance.Quantity,
                    CreatedAtUtc = now,
                    InventoryMovementId = movement.Id
                });
            }

            if (movement.DestinationStorageLocationId is not null)
            {
                var destinationKey = (movement.WarehouseId, movement.DestinationStorageLocationId.Value, movement.StockKeepingUnitId);
                var destinationBalanceCreated = false;

                if (!balances.TryGetValue(destinationKey, out var destinationBalance))
                {
                    destinationBalanceCreated = true;

                    destinationBalance = new InventoryBalance
                    {
                        WarehouseId = movement.WarehouseId,
                        StorageLocationId = movement.DestinationStorageLocationId.Value,
                        StockKeepingUnitId = movement.StockKeepingUnitId,
                        Quantity = 0,
                        CreatedAtUtc = now
                    };

                    dbContext.InventoryBalances.Add(destinationBalance);
                    balances.Add(destinationKey, destinationBalance);
                }

                var balanceBefore = destinationBalance.Quantity;
                destinationBalance.Quantity += movement.Quantity;

                if (!destinationBalanceCreated)
                    destinationBalance.UpdatedAtUtc = now;

                dbContext.InventoryTurnovers.Add(new InventoryTurnover
                {
                    WarehouseId = movement.WarehouseId,
                    StorageLocationId = movement.DestinationStorageLocationId.Value,
                    StockKeepingUnitId = movement.StockKeepingUnitId,
                    BalanceBefore = balanceBefore,
                    QuantityDelta = movement.Quantity,
                    BalanceAfter = destinationBalance.Quantity,
                    CreatedAtUtc = now,
                    InventoryMovementId = movement.Id
                });
            }

            movement.PostedAtUtc = now;
        }

        return OperationResult.Success();
    }
}

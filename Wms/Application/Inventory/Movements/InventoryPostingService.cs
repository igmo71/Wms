using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wms.Common;
using Wms.Data;
using Wms.Domain;

namespace Wms.Application.Inventory.Movements;

public class InventoryPostingService(ILogger<InventoryPostingService> logger)
{
    internal async Task<OperationResult> PostInventoryMovementsAsync(
        IReadOnlyCollection<InventoryMovement> movements,
        ApplicationDbContext dbContext,
        CancellationToken ct)
    {
        using var scope = logger.BeginScope(
            "BalanceAndTurnover PostInventoryMovements {Count}",
            movements.Count);

        var locationResult = await ValidateLocationsAsync(movements, dbContext, ct);
        if (!locationResult.IsSuccess)
        {
            return locationResult;
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var movement in movements)
        {
            var postResult = movement.Post(now);
            if (!postResult.IsSuccess)
            {
                logger.LogError("Движение запасов не проведено. Движение: {MovementId}, ошибка: {ErrorMessage}",
                    movement.Id, postResult.Error?.Message);
                return postResult;
            }
        }

        var warehouseIds = movements.Select(x => x.WarehouseId).Distinct().ToArray();
        var storageLocationIds = GetStorageLocationIds(movements);
        var stockKeepingUnitIds = movements.Select(x => x.StockKeepingUnitId).Distinct().ToArray();

        var balances = await dbContext.InventoryBalances
            .Where(x => warehouseIds.Contains(x.WarehouseId)
                && storageLocationIds.Contains(x.StorageLocationId)
                && stockKeepingUnitIds.Contains(x.StockKeepingUnitId))
            .ToDictionaryAsync(
                x => (x.WarehouseId, x.StorageLocationId, x.StockKeepingUnitId),
                ct);

        foreach (var movement in movements)
        {
            var movementResult = ApplyMovement(movement, balances, dbContext, now);
            if (!movementResult.IsSuccess)
            {
                return movementResult;
            }
        }

        return OperationResult.Success();
    }

    private OperationResult ApplyMovement(
        InventoryMovement movement,
        Dictionary<(Guid WarehouseId, Guid StorageLocationId, Guid StockKeepingUnitId), InventoryBalance> balances,
        ApplicationDbContext dbContext,
        DateTimeOffset occurredAtUtc)
    {
        if (movement.SourceStorageLocationId is Guid sourceStorageLocationId)
        {
            var sourceResult = ApplySource(
                movement,
                sourceStorageLocationId,
                balances,
                dbContext,
                occurredAtUtc);
            if (!sourceResult.IsSuccess)
            {
                return sourceResult;
            }
        }

        if (movement.DestinationStorageLocationId is Guid destinationStorageLocationId)
        {
            var destinationResult = ApplyDestination(
                movement,
                destinationStorageLocationId,
                balances,
                dbContext,
                occurredAtUtc);
            if (!destinationResult.IsSuccess)
            {
                return destinationResult;
            }
        }

        return OperationResult.Success();
    }

    private OperationResult ApplySource(
        InventoryMovement movement,
        Guid storageLocationId,
        Dictionary<(Guid WarehouseId, Guid StorageLocationId, Guid StockKeepingUnitId), InventoryBalance> balances,
        ApplicationDbContext dbContext,
        DateTimeOffset occurredAtUtc)
    {
        var key = (movement.WarehouseId, storageLocationId, movement.StockKeepingUnitId);
        if (!balances.TryGetValue(key, out var balance))
        {
            logger.LogError("Остаток в позиции-источнике не найден. Движение: {MovementId}", movement.Id);
            return OperationError.Failure("Остаток в позиции-источнике не найден.");
        }

        if (balance.Quantity < movement.Quantity)
        {
            logger.LogError("Недостаточно остатка в позиции-источнике. Движение: {MovementId}", movement.Id);
            return OperationError.Failure("Недостаточно остатка в позиции-источнике.");
        }

        var changeResult = balance.Adjust(-movement.Quantity, occurredAtUtc);
        if (!changeResult.IsSuccess)
        {
            return changeResult.Error!;
        }

        return AddTurnover(
            movement,
            storageLocationId,
            changeResult.Value,
            occurredAtUtc,
            dbContext);
    }

    private static OperationResult ApplyDestination(
        InventoryMovement movement,
        Guid storageLocationId,
        Dictionary<(Guid WarehouseId, Guid StorageLocationId, Guid StockKeepingUnitId), InventoryBalance> balances,
        ApplicationDbContext dbContext,
        DateTimeOffset occurredAtUtc)
    {
        var key = (movement.WarehouseId, storageLocationId, movement.StockKeepingUnitId);
        InventoryBalanceChange change;

        if (balances.TryGetValue(key, out var balance))
        {
            var changeResult = balance.Adjust(movement.Quantity, occurredAtUtc);
            if (!changeResult.IsSuccess)
            {
                return changeResult.Error!;
            }

            change = changeResult.Value;
        }
        else
        {
            var balanceResult = InventoryBalance.Create(
                Guid.NewGuid(),
                movement.WarehouseId,
                storageLocationId,
                movement.StockKeepingUnitId,
                movement.Quantity,
                occurredAtUtc);
            if (!balanceResult.IsSuccess)
            {
                return balanceResult.Error!;
            }

            balance = balanceResult.Value!;
            change = new InventoryBalanceChange(0, movement.Quantity, movement.Quantity);
            balances.Add(key, balance);
            dbContext.InventoryBalances.Add(balance);
        }

        return AddTurnover(
            movement,
            storageLocationId,
            change,
            occurredAtUtc,
            dbContext);
    }

    private static OperationResult AddTurnover(
        InventoryMovement movement,
        Guid storageLocationId,
        InventoryBalanceChange change,
        DateTimeOffset occurredAtUtc,
        ApplicationDbContext dbContext)
    {
        var turnoverResult = InventoryTurnover.Create(
            Guid.NewGuid(),
            movement.WarehouseId,
            storageLocationId,
            movement.StockKeepingUnitId,
            change,
            occurredAtUtc,
            movement.Id);
        if (!turnoverResult.IsSuccess)
        {
            return turnoverResult.Error!;
        }

        dbContext.InventoryTurnovers.Add(turnoverResult.Value!);
        return OperationResult.Success();
    }

    private static async Task<OperationResult> ValidateLocationsAsync(
        IReadOnlyCollection<InventoryMovement> movements,
        ApplicationDbContext dbContext,
        CancellationToken ct)
    {
        var storageLocationIds = GetStorageLocationIds(movements);
        var locations = await dbContext.StorageLocations
            .Where(x => storageLocationIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);

        foreach (var movement in movements)
        {
            var movementLocationIds = new[]
            {
                movement.SourceStorageLocationId,
                movement.DestinationStorageLocationId
            };

            foreach (var locationId in movementLocationIds.Where(x => x.HasValue).Select(x => x!.Value))
            {
                if (!locations.TryGetValue(locationId, out var location)
                    || location.IsFolder
                    || location.WarehouseId != movement.WarehouseId)
                {
                    return OperationError.Invalid(
                        "Для движений необходимы складские позиции, не являющиеся папками и принадлежащие своему складу.");
                }
            }
        }

        return OperationResult.Success();
    }

    private static Guid[] GetStorageLocationIds(IReadOnlyCollection<InventoryMovement> movements)
    {
        return movements
            .SelectMany(x => new[]
            {
                x.SourceStorageLocationId,
                x.DestinationStorageLocationId
            })
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .Distinct()
            .ToArray();
    }
}

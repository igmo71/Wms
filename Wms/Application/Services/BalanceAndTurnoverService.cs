using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wms.Common;
using Wms.Data;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Application.Services;

public class BalanceAndTurnoverService(ILogger<BalanceAndTurnoverService> logger)
{
    internal async Task<ServiceResult> PostInventoryMovementsAsync(
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
                return ServiceError.Invalid("Inventory movement quantity must be greater than zero.");
            }

            if (movement.SourceStorageLocationId is null && movement.DestinationStorageLocationId is null)
            {
                logger.LogError("Inventory movement source or destination must be specified. Movement: {MovementId}", movement.Id);
                return ServiceError.Invalid("Inventory movement source or destination must be specified.");
            }

            if (movement.SourceStorageLocationId == movement.DestinationStorageLocationId)
            {
                logger.LogError("Inventory movement source and destination must be different. Movement: {MovementId}", movement.Id);
                return ServiceError.Invalid("Inventory movement source and destination must be different.");
            }

            if (movement.PostedAtUtc is not null)
            {
                logger.LogError("Inventory movement has already been posted. Movement: {MovementId}", movement.Id);
                return ServiceError.Failure("Inventory movement has already been posted.");
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
                    return ServiceError.Failure("Source inventory balance not found.");
                }

                if (sourceBalance.Quantity < movement.Quantity)
                {
                    logger.LogError("Insufficient source inventory balance. Movement: {MovementId}", movement.Id);
                    return ServiceError.Failure("Insufficient source inventory balance.");
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
                    RecorderId = movement.RecorderId,
                    RecorderLineNumber = movement.RecorderLineNumber,
                    RecorderType = movement.RecorderType
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
                    RecorderId = movement.RecorderId,
                    RecorderLineNumber = movement.RecorderLineNumber,
                    RecorderType = movement.RecorderType
                });
            }

            movement.PostedAtUtc = now;
        }

        return ServiceResult.Success();
    }

    public async Task<ServiceResult> PostReceivedOrderInventoryAsync(ReceivingOrder receivingOrder, ApplicationDbContext dbContext, CancellationToken ct)
    {
        using var scope = logger.BeginScope("BalanceAndTurnover CreateOrUpdate {OrderId} {WarehouseId} {StorageLocationId}",
            receivingOrder.Id, receivingOrder.WarehouseId, receivingOrder.ReceivingLocationId);

        if (receivingOrder.ReceivingLocationId is null)
        {
            logger.LogError("Receiving location must be specified");
            return ServiceError.Invalid("Receiving location must be specified");
        }

        Guid receivingLocationId = (Guid)receivingOrder.ReceivingLocationId;

        var now = DateTimeOffset.UtcNow;

        var skuIds = receivingOrder.Items
            .Select(x => x.StockKeepingUnitId)
            .Distinct()
            .ToArray();

        var balances = await dbContext.InventoryBalances
            .Where(x =>
                x.WarehouseId == receivingOrder.WarehouseId &&
                x.StorageLocationId == receivingLocationId &&
                skuIds.Contains(x.StockKeepingUnitId))
            .ToDictionaryAsync(x => x.StockKeepingUnitId, ct);

        foreach (var item in receivingOrder.Items)
        {
            if (item.FactQuantity < 0)
            {
                logger.LogError("Fact quantity cannot be negative for line {LineNumber}", item.LineNumber);
                return ServiceError.Invalid("Fact quantity cannot be negative for line {item.LineNumber}");
            }

            if (item.FactQuantity == 0)
                continue;

            // TODO: Если разрешать перепроводить документ, то нужно сначала реализовать откат и InventoryBalance, и InventoryTurnover
            var alreadyPosted = await dbContext.InventoryTurnovers
                .AnyAsync(x => x.RecorderId == receivingOrder.Id, ct);
            if (alreadyPosted)
            {
                logger.LogError("Receiving order has already been posted.");
                return ServiceError.Failure("Receiving order has already been posted");
            }

            double balanceBefore;

            if (!balances.TryGetValue(item.StockKeepingUnitId, out var balance))
            {
                balanceBefore = 0;

                balance = new InventoryBalance
                {
                    StockKeepingUnitId = item.StockKeepingUnitId,
                    StorageLocationId = receivingLocationId,
                    WarehouseId = receivingOrder.WarehouseId,
                    Quantity = item.FactQuantity,
                    CreatedAtUtc = now
                };

                dbContext.InventoryBalances.Add(balance);

                balances.Add(item.StockKeepingUnitId, balance);

                logger.LogDebug("Created InventoryBalance for SKU {SkuId} with quantity {Quantity}",
                    item.StockKeepingUnitId, balance.Quantity);
            }
            else
            {
                balanceBefore = balance.Quantity;

                balance.Quantity += item.FactQuantity;

                balance.UpdatedAtUtc = now;

                logger.LogDebug("Updated InventoryBalance for SKU {SkuId} from {QuantityBefore} to {QuantityAfter}",
                    item.StockKeepingUnitId, balanceBefore, balance.Quantity);
            }

            var turnover = new InventoryTurnover
            {
                StockKeepingUnitId = item.StockKeepingUnitId,
                WarehouseId = receivingOrder.WarehouseId,
                StorageLocationId = receivingLocationId,
                BalanceBefore = balanceBefore,
                QuantityDelta = item.FactQuantity,
                BalanceAfter = balance.Quantity,
                CreatedAtUtc = now,
                RecorderId = receivingOrder.Id,
                RecorderLineNumber = item.LineNumber,
                RecorderType = RecorderType.ReceivingOrder
            };

            dbContext.InventoryTurnovers.Add(turnover);

            logger.LogDebug("Created InventoryTurnover for SKU {SkuId}, delta {QuantityDelta}, balance {BalanceAfter}",
                item.StockKeepingUnitId, turnover.QuantityDelta, turnover.BalanceAfter);
        }

        return ServiceResult.Success();
    }

    internal async Task<ServiceResult> ShipShippingOrder(ShippingOrder shippingOrder, ApplicationDbContext dbContext, CancellationToken ct)
    {
        using var scope = logger.BeginScope("BalanceAndTurnover CreateOrUpdate {OrderId} {WarehouseId} {StorageLocationId}",
            shippingOrder.Id, shippingOrder.WarehouseId, shippingOrder.ShippingLocationId);

        if (shippingOrder.ShippingLocationId is null)
        {
            logger.LogError("Shipping location must be specified");
            return ServiceError.Invalid("Shipping location must be specified");
        }

        var shippingLocationId = (Guid)shippingOrder.ShippingLocationId;
        var now = DateTimeOffset.UtcNow;

        var skuIds = shippingOrder.Items
            .Select(x => x.StockKeepingUnitId)
            .Distinct()
            .ToArray();

        var balances = await dbContext.InventoryBalances
            .Where(x =>
                x.WarehouseId == shippingOrder.WarehouseId &&
                x.StorageLocationId == shippingLocationId &&
                skuIds.Contains(x.StockKeepingUnitId))
            .ToDictionaryAsync(x => x.StockKeepingUnitId, ct);

        var alreadyPosted = await dbContext.InventoryTurnovers
            .AnyAsync(x => x.RecorderId == shippingOrder.Id, ct);

        if (alreadyPosted)
        {
            logger.LogError("Shipping order has already been posted.");
            return ServiceError.Failure("Shipping order has already been posted");
        }

        foreach (var item in shippingOrder.Items)
        {
            if (item.FactQuantity < 0)
            {
                logger.LogError("Fact quantity cannot be negative for line {LineNumber}", item.LineNumber);
                return ServiceError.Invalid("Fact quantity cannot be negative for line {item.LineNumber}");
            }

            if (item.FactQuantity == 0)
                continue;

            if (!balances.TryGetValue(item.StockKeepingUnitId, out var balance))
            {
                logger.LogError("Inventory balance not found for SKU {SkuId}", item.StockKeepingUnitId);
                return ServiceError.Failure("Inventory balance not found for shipping order item");
            }

            if (balance.Quantity < item.FactQuantity)
            {
                logger.LogError("Insufficient inventory balance for SKU {SkuId}", item.StockKeepingUnitId);
                return ServiceError.Failure("Insufficient inventory balance for shipping order item");
            }

            var balanceBefore = balance.Quantity;
            balance.Quantity -= item.FactQuantity;
            balance.UpdatedAtUtc = now;

            var turnover = new InventoryTurnover
            {
                StockKeepingUnitId = item.StockKeepingUnitId,
                WarehouseId = shippingOrder.WarehouseId,
                StorageLocationId = shippingLocationId,
                BalanceBefore = balanceBefore,
                QuantityDelta = -item.FactQuantity,
                BalanceAfter = balance.Quantity,
                CreatedAtUtc = now,
                RecorderId = shippingOrder.Id,
                RecorderLineNumber = item.LineNumber,
                RecorderType = RecorderType.ShippingOrder
            };

            dbContext.InventoryTurnovers.Add(turnover);

            logger.LogDebug("Updated InventoryBalance for SKU {SkuId} from {QuantityBefore} to {QuantityAfter}",
                item.StockKeepingUnitId, balanceBefore, balance.Quantity);
        }

        return ServiceResult.Success();
    }
}

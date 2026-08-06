using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wms.Data;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Application;

public class BalanceAndTurnoverService(ILogger<BalanceAndTurnoverService> logger)
{
    public async Task CompleteReceivingOrder(ReceivingOrder receivingOrder, ApplicationDbContext dbContext, CancellationToken ct)
    {
        using var scope = logger.BeginScope("BalanceAndTurnover CreateOrUpdate {OrderId} {WarehouseId} {StorageLocationId}",
            receivingOrder.Id, receivingOrder.WarehouseId, receivingOrder.ReceivingLocationId);

        var receivingLocationId = receivingOrder.ReceivingLocationId
            ?? throw new InvalidOperationException("Receiving location must be specified.");

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
            if (item.FactQuantity < 0m)
                throw new InvalidOperationException($"Fact quantity cannot be negative for line {item.LineNumber}.");

            if (item.FactQuantity == 0m)
                continue;

            decimal balanceBefore;

            if (!balances.TryGetValue(item.StockKeepingUnitId, out var balance))
            {
                balanceBefore = 0m;

                balance = new InventoryBalance
                {
                    StockKeepingUnitId = item.StockKeepingUnitId,
                    StorageLocationId = receivingLocationId,
                    WarehouseId = receivingOrder.WarehouseId,
                    Quantity = item.FactQuantity
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

                logger.LogDebug("Updated InventoryBalance for SKU {SkuId} from {QuantityBefore} to {QuantityAfter}",
                    item.StockKeepingUnitId, balanceBefore, balance.Quantity);
            }

            var alreadyPosted = await dbContext.InventoryTurnovers
                .AnyAsync(x =>
                    x.RecorderId == receivingOrder.Id &&
                    x.RecorderType == RecorderType.ReceivingOrder,
                    ct);

            if (alreadyPosted)
                throw new InvalidOperationException("Receiving order has already been posted.");

            var turnover = new InventoryTurnover
            {
                StockKeepingUnitId = item.StockKeepingUnitId,
                WarehouseId = receivingOrder.WarehouseId,
                StorageLocationId = receivingLocationId,
                BalanceBefore = balanceBefore,
                QuantityDelta = item.FactQuantity,
                BalanceAfter = balance.Quantity,
                DateTimeUtc = now,
                RecorderId = receivingOrder.Id,
                RecorderType = RecorderType.ReceivingOrder,
                RecorderLineNumber = item.LineNumber
            };

            dbContext.InventoryTurnovers.Add(turnover);

            logger.LogDebug("Created InventoryTurnover for SKU {SkuId}, delta {QuantityDelta}, balance {BalanceAfter}",
                item.StockKeepingUnitId, turnover.QuantityDelta, turnover.BalanceAfter);
        }
    }
}

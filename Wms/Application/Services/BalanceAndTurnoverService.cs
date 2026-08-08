using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wms.Common;
using Wms.Data;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Application.Services;

public class BalanceAndTurnoverService(ILogger<BalanceAndTurnoverService> logger)
{
    public async Task<ServiceResult> CompleteReceivingOrder(ReceivingOrder receivingOrder, ApplicationDbContext dbContext, CancellationToken ct)
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

            var alreadyPosted = await dbContext.InventoryTurnovers
                .AnyAsync(x => x.RecorderId == receivingOrder.Id, ct);
            // TODO: Если разрешать перепроводить документ, то нужно сначала реализовать откат и InventoryBalance, и InventoryTurnover
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
}

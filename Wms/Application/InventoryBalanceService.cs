using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wms.Data;
using Wms.Domain;

namespace Wms.Application;

public class InventoryBalanceService(ILogger<InventoryBalanceService> logger)
{
    public async Task CreateOrUpdateAsync(ReceivingOrder receivingOrder, ApplicationDbContext dbContext, CancellationToken ct)
    {
        using var scope = logger.BeginScope(new Dictionary<string, object>
        {
            ["Source"] = nameof(CreateOrUpdateAsync),
            ["OrderId"] = receivingOrder.Id,
            ["WarehouseId"] = receivingOrder.WarehouseId,
            ["StorageLocationId"] = receivingOrder.ReceivingLocationId
        });

        foreach (var item in receivingOrder.Items)
        {
            var existingBalance = await dbContext.InventoryBalances
                .FirstOrDefaultAsync(x =>
                    x.StockKeepingUnitId == item.StockKeepingUnitId &&
                    x.StorageLocationId == receivingOrder.ReceivingLocationId &&
                    x.WarehouseId == receivingOrder.WarehouseId,
                    ct);

            if (existingBalance is null)
            {
                var newInventoryBalance = new InventoryBalance
                {
                    StockKeepingUnitId = item.StockKeepingUnitId,
                    StorageLocationId = receivingOrder.ReceivingLocationId,
                    WarehouseId = receivingOrder.WarehouseId,
                    Quantity = item.FactQuantity
                };

                dbContext.Add(newInventoryBalance);

                if (logger.IsEnabled(LogLevel.Debug))
                    logger.LogDebug("Created new InventoryBalance for SKU {StockKeepingUnitId} {@InventoryBalance}",
                        item.StockKeepingUnitId, newInventoryBalance);
            }
            else
            {
                var quantityBefore = existingBalance.Quantity;

                existingBalance.Quantity += item.FactQuantity;

                if (logger.IsEnabled(LogLevel.Debug))
                    logger.LogDebug("Updated existing InventoryBalance for SKU {StockKeepingUnitId} {QuantityBefore} to {QuantityAfter}",
                    item.StockKeepingUnitId, quantityBefore, existingBalance.Quantity);
            }
        }
    }
}

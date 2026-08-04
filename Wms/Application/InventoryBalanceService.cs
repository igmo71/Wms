using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wms.Data;
using Wms.Domain;

namespace Wms.Application;

public class InventoryBalanceService(ILogger<InventoryBalanceService> logger)
{
    public async Task CreateOrUpdateAsync(ReceivingOrder existingOrder, ApplicationDbContext dbContext, CancellationToken ct)
    {
        using var scope = logger.BeginScope(new Dictionary<string, object>
        {
            ["Source"] = nameof(CreateOrUpdateAsync),
            ["OrderId"] = existingOrder.Id,
            ["WarehouseId"] = existingOrder.WarehouseId,
            ["StorageLocationId"] = existingOrder.ReceivingLocationId
        });

        foreach (var item in existingOrder.Items)
        {
            var existingBalance = await dbContext.InventoryBalances
                .FirstOrDefaultAsync(x =>
                    x.StockKeepingUnitId == item.StockKeepingUnitId &&
                    x.StorageLocationId == existingOrder.ReceivingLocationId &&
                    x.WarehouseId == existingOrder.WarehouseId,
                    ct);

            if (existingBalance is null)
            {
                var newInventoryBalance = new InventoryBalance
                {
                    StockKeepingUnitId = item.StockKeepingUnitId,
                    StorageLocationId = existingOrder.ReceivingLocationId,
                    WarehouseId = existingOrder.WarehouseId,
                    Quantity = item.FactQuantity
                };

                dbContext.Add(newInventoryBalance);

                if (logger.IsEnabled(LogLevel.Debug))
                    logger.LogDebug("Created new inventory balance for SKU {StockKeepingUnitId} {@InventoryBalance}",
                        item.StockKeepingUnitId, newInventoryBalance);
            }
            else
            {
                var oldQty = existingBalance.Quantity;

                existingBalance.Quantity += item.FactQuantity;

                if (logger.IsEnabled(LogLevel.Debug))
                    logger.LogDebug("Updated existing inventory balance for SKU {StockKeepingUnitId} {oldQty} to {newQty}",
                    item.StockKeepingUnitId, oldQty, existingBalance.Quantity);
            }
        }
    }
}

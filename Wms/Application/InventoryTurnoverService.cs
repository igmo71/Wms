using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wms.Data;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Application;

public class InventoryTurnoverService(ILogger<InventoryTurnoverService> logger)
{
    public async Task CreateAsync(ReceivingOrder receivingOrder, ApplicationDbContext dbContext, CancellationToken ct)
    {
        using var scope = logger.BeginScope(new Dictionary<string, object>
        {
            ["Source"] = nameof(CreateAsync),
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

            var balanceBefore = existingBalance is null ? 0 : existingBalance.Quantity;

            var newInventoryTurnover = new InventoryTurnover
            {
                WarehouseId = receivingOrder.WarehouseId,
                StorageLocationId = receivingOrder.ReceivingLocationId,
                BalanceBefore = balanceBefore,
                QuantityDelta = item.FactQuantity,
                BalanceAfter = balanceBefore + item.FactQuantity,
                DateTimeUtc = DateTimeOffset.UtcNow,
                RecorderId = receivingOrder.Id,
                RecorderType = RecorderType.ReceivingOrder,
                RecorderLineNumber = item.LineNumber,
            };

            await dbContext.InventoryTurnovers.AddAsync(newInventoryTurnover, ct);

            if (logger.IsEnabled(LogLevel.Debug))
                logger.LogDebug("Created new InventoryTurnover for SKU {StockKeepingUnitId} {@InventoryBalance}",
                    item.StockKeepingUnitId, newInventoryTurnover);
        }
    }
}

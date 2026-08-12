using Microsoft.EntityFrameworkCore;
using Wms.Common;
using Wms.Data;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Application.Services.Inventory;

public class InventoryCountCommandService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    BalanceAndTurnoverService balanceAndTurnoverService)
{
    public async Task<ServiceResult<InventoryCount>> CreateAsync(Guid warehouseId, CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        if (!await dbContext.Warehouses.AnyAsync(x => x.Id == warehouseId, ct))
            return ServiceError.NotFound<Warehouse>();

        var inventoryCount = new InventoryCount
        {
            Id = Guid.NewGuid(),
            WarehouseId = warehouseId,
            Status = InventoryCountStatus.Draft,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        dbContext.InventoryCounts.Add(inventoryCount);
        await dbContext.SaveChangesAsync(ct);

        return inventoryCount;
    }

    public async Task<ServiceResult> AddItemAsync(Guid inventoryCountId, CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var inventoryCount = await dbContext.InventoryCounts
            .FirstOrDefaultAsync(x => x.Id == inventoryCountId, ct);

        if (inventoryCount is null)
            return ServiceError.NotFound<InventoryCount>();

        if (inventoryCount.Status != InventoryCountStatus.Draft)
            return ServiceError.Invalid<InventoryCount>("Items can be added only to a draft inventory count.");

        var lastLineNumber = await dbContext.InventoryCountItems
            .Where(x => x.InventoryCountId == inventoryCountId)
            .Select(x => (int?)x.LineNumber)
            .MaxAsync(ct) ?? 0;

        dbContext.InventoryCountItems.Add(new InventoryCountItem
        {
            Id = Guid.NewGuid(),
            InventoryCountId = inventoryCount.Id,
            LineNumber = lastLineNumber + 1
        });

        inventoryCount.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(ct);

        return ServiceResult.Success();
    }

    public async Task<ServiceResult> UpdateItemAsync(
        Guid itemId,
        Guid? storageLocationId,
        Guid? stockKeepingUnitId,
        double countedQuantity,
        CancellationToken ct = default)
    {
        if (countedQuantity < 0)
            return ServiceError.Invalid<InventoryCountItem>("Counted quantity cannot be negative.");

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var item = await dbContext.InventoryCountItems
            .Include(x => x.InventoryCount)
            .FirstOrDefaultAsync(x => x.Id == itemId, ct);

        if (item is null)
            return ServiceError.NotFound<InventoryCountItem>();

        var inventoryCount = item.InventoryCount!;
        if (inventoryCount.Status != InventoryCountStatus.Draft)
            return ServiceError.Invalid<InventoryCount>("Items can be changed only in a draft inventory count.");

        if (storageLocationId is Guid locationId)
        {
            var storageLocation = await dbContext.StorageLocations
                .FirstOrDefaultAsync(x => x.Id == locationId, ct);

            if (storageLocation is null)
                return ServiceError.NotFound<StorageLocation>();

            if (storageLocation.WarehouseId != inventoryCount.WarehouseId)
                return ServiceError.Invalid<StorageLocation>("Storage location must belong to the inventory count warehouse.");
        }

        if (stockKeepingUnitId is Guid skuId
            && !await dbContext.StockKeepingUnits.AnyAsync(x => x.Id == skuId, ct))
        {
            return ServiceError.NotFound<StockKeepingUnit>();
        }

        if (storageLocationId is not null && stockKeepingUnitId is not null)
        {
            var duplicateExists = await dbContext.InventoryCountItems
                .AnyAsync(x => x.InventoryCountId == inventoryCount.Id
                    && x.Id != item.Id
                    && x.StorageLocationId == storageLocationId
                    && x.StockKeepingUnitId == stockKeepingUnitId, ct);

            if (duplicateExists)
                return ServiceError.Invalid<InventoryCountItem>("Storage location and SKU combination must be unique within the inventory count.");
        }

        item.StorageLocationId = storageLocationId;
        item.StockKeepingUnitId = stockKeepingUnitId;
        item.CountedQuantity = countedQuantity;
        item.ExpectedQuantity = 0;

        if (storageLocationId is Guid inventoryLocationId && stockKeepingUnitId is Guid inventorySkuId)
        {
            item.ExpectedQuantity = await dbContext.InventoryBalances
                .Where(x => x.WarehouseId == inventoryCount.WarehouseId
                    && x.StorageLocationId == inventoryLocationId
                    && x.StockKeepingUnitId == inventorySkuId)
                .Select(x => (double?)x.Quantity)
                .FirstOrDefaultAsync(ct) ?? 0;
        }

        var now = DateTimeOffset.UtcNow;
        item.UpdatedAtUtc = now;
        inventoryCount.UpdatedAtUtc = now;

        await dbContext.SaveChangesAsync(ct);
        return ServiceResult.Success();
    }

    public async Task<ServiceResult> DeleteItemAsync(Guid itemId, CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var item = await dbContext.InventoryCountItems
            .Include(x => x.InventoryCount)
            .FirstOrDefaultAsync(x => x.Id == itemId, ct);

        if (item is null)
            return ServiceError.NotFound<InventoryCountItem>();

        var inventoryCount = item.InventoryCount!;
        if (inventoryCount.Status != InventoryCountStatus.Draft)
            return ServiceError.Invalid<InventoryCount>("Items can be deleted only from a draft inventory count.");

        dbContext.InventoryCountItems.Remove(item);
        inventoryCount.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(ct);
        return ServiceResult.Success();
    }

    public async Task<ServiceResult> PostAsync(Guid inventoryCountId, CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var inventoryCount = await dbContext.InventoryCounts
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == inventoryCountId, ct);

        if (inventoryCount is null)
            return ServiceError.NotFound<InventoryCount>();

        if (inventoryCount.Status != InventoryCountStatus.Draft)
            return ServiceError.Invalid<InventoryCount>("Only a draft inventory count can be posted.");

        if (inventoryCount.Items.Any(x => x.StorageLocationId is null || x.StockKeepingUnitId is null))
            return ServiceError.Invalid<InventoryCountItem>("Every inventory count item must have a storage location and SKU before posting.");

        var hasDuplicates = inventoryCount.Items
            .GroupBy(x => new { x.StorageLocationId, x.StockKeepingUnitId })
            .Any(x => x.Count() > 1);

        if (hasDuplicates)
            return ServiceError.Invalid<InventoryCountItem>("Storage location and SKU combination must be unique within the inventory count.");

        var now = DateTimeOffset.UtcNow;
        var movements = inventoryCount.Items
            .Where(x => x.DifferenceQuantity != 0)
            .Select(x => new InventoryMovement
            {
                Id = Guid.NewGuid(),
                WarehouseId = inventoryCount.WarehouseId,
                SourceStorageLocationId = x.DifferenceQuantity < 0 ? x.StorageLocationId : null,
                DestinationStorageLocationId = x.DifferenceQuantity > 0 ? x.StorageLocationId : null,
                StockKeepingUnitId = x.StockKeepingUnitId!.Value,
                Quantity = Math.Abs(x.DifferenceQuantity),
                CreatedAtUtc = now,
                RecorderType = RecorderType.InventoryCount,
                RecorderId = inventoryCount.Id,
                RecorderLineNumber = x.LineNumber
            })
            .ToList();

        dbContext.InventoryMovements.AddRange(movements);

        if (movements.Count > 0)
        {
            var postingResult = await balanceAndTurnoverService
                .PostInventoryMovementsAsync(movements, dbContext, ct);

            if (!postingResult.IsSuccess)
                return postingResult;
        }

        inventoryCount.Status = InventoryCountStatus.Posted;
        inventoryCount.PostedAtUtc = now;
        inventoryCount.UpdatedAtUtc = now;

        await dbContext.SaveChangesAsync(ct);
        return ServiceResult.Success();
    }
}

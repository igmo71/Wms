using Microsoft.EntityFrameworkCore;
using Wms.Common;
using Wms.Data;
using Wms.Domain;
using Wms.Domain.Enums;

using Wms.Application.Inventory.Movements;

namespace Wms.Application.Inventory.Counts;

public class InventoryCountCommandService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    InventoryPostingService inventoryPostingService)
{
    public async Task<OperationResult<InventoryCount>> CreateAsync(
        Guid warehouseId,
        string userId,
        CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var countResult = InventoryCount.Create(
            Guid.NewGuid(),
            now.LocalDateTime.ToString("yyMMdd-HHmmss"),
            now.LocalDateTime.Date,
            warehouseId,
            now,
            userId);
        if (!countResult.IsSuccess)
        {
            return countResult.Error!;
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        if (!await dbContext.Warehouses.AnyAsync(x => x.Id == warehouseId, ct))
        {
            return OperationError.NotFound($"Склад '{warehouseId}' не найден.");
        }

        var inventoryCount = countResult.Value!;
        dbContext.InventoryCounts.Add(inventoryCount);
        await dbContext.SaveChangesAsync(ct);
        return inventoryCount;
    }

    public async Task<OperationResult> AddItemAsync(
        Guid inventoryCountId,
        string userId,
        CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        var inventoryCount = await dbContext.InventoryCounts
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == inventoryCountId, ct);
        if (inventoryCount is null)
        {
            return OperationError.NotFound($"Инвентаризация '{inventoryCountId}' не найдена.");
        }

        var itemResult = inventoryCount.AddItem(Guid.NewGuid(), DateTimeOffset.UtcNow, userId);
        if (!itemResult.IsSuccess)
        {
            return itemResult.Error!;
        }

        dbContext.InventoryCountItems.Add(itemResult.Value!);
        await dbContext.SaveChangesAsync(ct);
        return OperationResult.Success();
    }

    public async Task<OperationResult> UpdateItemAsync(
        Guid itemId,
        Guid? storageLocationId,
        Guid? stockKeepingUnitId,
        double countedQuantity,
        string userId,
        CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        var inventoryCount = await FindByItemAsync(dbContext, itemId, ct);
        if (inventoryCount is null)
        {
            return OperationError.NotFound($"Строка инвентаризации '{itemId}' не найдена.");
        }

        var contextResult = await ValidateItemContextAsync(
            dbContext,
            inventoryCount,
            storageLocationId,
            stockKeepingUnitId,
            ct);
        if (!contextResult.IsSuccess)
        {
            return contextResult.Error!;
        }

        var updateResult = inventoryCount.UpdateItem(
            itemId,
            storageLocationId,
            stockKeepingUnitId,
            contextResult.Value,
            countedQuantity,
            DateTimeOffset.UtcNow,
            userId);
        if (!updateResult.IsSuccess)
        {
            return updateResult;
        }

        await dbContext.SaveChangesAsync(ct);
        return OperationResult.Success();
    }

    public async Task<OperationResult> DeleteItemAsync(
        Guid itemId,
        string userId,
        CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        var inventoryCount = await FindByItemAsync(dbContext, itemId, ct);
        if (inventoryCount is null)
        {
            return OperationError.NotFound($"Строка инвентаризации '{itemId}' не найдена.");
        }

        var removalResult = inventoryCount.RemoveItem(itemId, DateTimeOffset.UtcNow, userId);
        if (!removalResult.IsSuccess)
        {
            return removalResult;
        }

        await dbContext.SaveChangesAsync(ct);
        return OperationResult.Success();
    }

    public async Task<OperationResult> PostAsync(
        Guid inventoryCountId,
        string userId,
        CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        var inventoryCount = await dbContext.InventoryCounts
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == inventoryCountId, ct);
        if (inventoryCount is null)
        {
            return OperationError.NotFound($"Инвентаризация '{inventoryCountId}' не найдена.");
        }

        var now = DateTimeOffset.UtcNow;
        var postResult = inventoryCount.Post(now, userId);
        if (!postResult.IsSuccess)
        {
            return postResult;
        }

        var movementsResult = CreateDifferenceMovements(inventoryCount, now, userId);
        if (!movementsResult.IsSuccess)
        {
            return movementsResult.Error!;
        }

        var movements = movementsResult.Value!;
        dbContext.InventoryMovements.AddRange(movements);

        if (movements.Count > 0)
        {
            var postingResult = await inventoryPostingService
                .PostInventoryMovementsAsync(movements, dbContext, ct);
            if (!postingResult.IsSuccess)
            {
                return postingResult;
            }
        }

        await dbContext.SaveChangesAsync(ct);
        return OperationResult.Success();
    }

    private static Task<InventoryCount?> FindByItemAsync(
        ApplicationDbContext dbContext,
        Guid itemId,
        CancellationToken ct)
    {
        return dbContext.InventoryCounts
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Items.Any(item => item.Id == itemId), ct);
    }

    private static async Task<OperationResult<double>> ValidateItemContextAsync(
        ApplicationDbContext dbContext,
        InventoryCount inventoryCount,
        Guid? storageLocationId,
        Guid? stockKeepingUnitId,
        CancellationToken ct)
    {
        if (storageLocationId is Guid locationId)
        {
            var locationResult = await ValidateStorageLocationAsync(
                dbContext,
                inventoryCount.WarehouseId,
                locationId,
                ct);
            if (!locationResult.IsSuccess)
            {
                return locationResult.Error!;
            }
        }

        if (stockKeepingUnitId is Guid skuId
            && !await dbContext.StockKeepingUnits.AnyAsync(x => x.Id == skuId, ct))
        {
            return OperationError.NotFound($"Номенклатура '{skuId}' не найдена.");
        }

        if (storageLocationId is not Guid inventoryLocationId
            || stockKeepingUnitId is not Guid inventorySkuId)
        {
            return 0d;
        }

        return await dbContext.InventoryBalances
            .Where(x => x.WarehouseId == inventoryCount.WarehouseId
                && x.StorageLocationId == inventoryLocationId
                && x.StockKeepingUnitId == inventorySkuId)
            .Select(x => (double?)x.Quantity)
            .FirstOrDefaultAsync(ct) ?? 0d;
    }

    private static async Task<OperationResult> ValidateStorageLocationAsync(
        ApplicationDbContext dbContext,
        Guid warehouseId,
        Guid storageLocationId,
        CancellationToken ct)
    {
        var storageLocation = await dbContext.StorageLocations
            .Include(x => x.Zone)
            .FirstOrDefaultAsync(x => x.Id == storageLocationId, ct);
        if (storageLocation is null)
        {
            return OperationError.NotFound($"Складская позиция '{storageLocationId}' не найдена.");
        }

        if (storageLocation.IsFolder
            || storageLocation.DeletionMark
            || storageLocation.Zone?.DeletionMark == true)
        {
            return OperationError.Invalid(
                "Позиция инвентаризации должна быть активной складской позицией.");
        }

        if (storageLocation.WarehouseId != warehouseId)
        {
            return OperationError.Invalid(
                "Складская позиция должна принадлежать складу инвентаризации.");
        }

        return storageLocation.Zone?.Type == ZoneType.Storage
            ? OperationResult.Success()
            : OperationError.Invalid(
                "Позиция инвентаризации должна принадлежать зоне хранения.");
    }

    private static OperationResult<List<InventoryMovement>> CreateDifferenceMovements(
        InventoryCount inventoryCount,
        DateTimeOffset createdAtUtc,
        string confirmedBy)
    {
        var movements = new List<InventoryMovement>();
        foreach (var item in inventoryCount.Items.Where(x => x.DifferenceQuantity != 0))
        {
            var movementResult = InventoryMovement.Create(
                Guid.NewGuid(),
                inventoryCount.WarehouseId,
                item.DifferenceQuantity < 0 ? item.StorageLocationId : null,
                item.DifferenceQuantity > 0 ? item.StorageLocationId : null,
                item.StockKeepingUnitId!.Value,
                Math.Abs(item.DifferenceQuantity),
                createdAtUtc,
                RecorderType.InventoryCount,
                inventoryCount.Id,
                item.LineNumber,
                confirmedBy);
            if (!movementResult.IsSuccess)
            {
                return movementResult.Error!;
            }

            movements.Add(movementResult.Value!);
        }

        return movements;
    }
}

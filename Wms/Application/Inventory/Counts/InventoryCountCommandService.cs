using Microsoft.EntityFrameworkCore;
using Wms.Application.Inventory.Movements;
using Wms.Application.Persistence;
using Wms.Common;
using Wms.Data;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Application.Inventory.Counts;

public sealed class InventoryCountCommandService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    InventoryPostingService inventoryPostingService)
{
    public async Task<OperationResult<InventoryCount>> CreateAsync(
        Guid warehouseId,
        Guid storageLocationId,
        string userId,
        CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        var result = await StageCreateAsync(dbContext, warehouseId, storageLocationId, userId, ct);
        if (!result.IsSuccess)
            return result.Error!;

        var saveResult = await ApplicationPersistence.SaveChangesAsync(dbContext, ct);
        return saveResult.IsSuccess ? result.Value! : saveResult.Error!;
    }

    internal async Task<OperationResult<InventoryCount>> StageCreateAsync(
        ApplicationDbContext dbContext,
        Guid warehouseId,
        Guid storageLocationId,
        string userId,
        CancellationToken ct)
    {
        var location = await dbContext.StorageLocations
            .Include(x => x.Warehouse)
            .Include(x => x.Zone)
            .Include(x => x.ActiveLock)
            .SingleOrDefaultAsync(x => x.Id == storageLocationId, ct);
        if (location is null)
            return OperationError.NotFound($"Складская позиция '{storageLocationId}' не найдена.");
        if (location.WarehouseId != warehouseId
            || location.Warehouse is null
            || location.Warehouse.DeletionMark
            || location.Zone is null
            || location.Zone.DeletionMark
            || location.Zone.Type != ZoneType.Storage
            || location.IsFolder
            || location.DeletionMark)
            return OperationError.Invalid("Инвентаризацию можно начать только для активной ячейки зоны хранения выбранного склада.");
        if (location.ActiveLock is not null)
            return OperationError.Conflict($"Ячейка {GetAddress(location)} уже заблокирована: {location.ActiveLock.Reason}");

        var now = DateTimeOffset.UtcNow;
        var countResult = InventoryCount.Create(
            Guid.NewGuid(),
            now.LocalDateTime.ToString("yyMMdd-HHmmss"),
            now.LocalDateTime.Date,
            warehouseId,
            storageLocationId,
            now,
            userId);
        if (!countResult.IsSuccess)
            return countResult.Error!;

        var inventoryCount = countResult.Value!;
        var expectedBalances = await dbContext.InventoryBalances
            .AsNoTracking()
            .Where(x => x.WarehouseId == warehouseId
                && x.StorageLocationId == storageLocationId
                && x.Quantity > 0)
            .OrderBy(x => x.StockKeepingUnitId)
            .Select(x => new { x.StockKeepingUnitId, x.Quantity })
            .ToListAsync(ct);

        foreach (var balance in expectedBalances)
        {
            var itemResult = inventoryCount.AddExpectedItem(
                Guid.NewGuid(),
                balance.StockKeepingUnitId,
                balance.Quantity,
                now,
                userId);
            if (!itemResult.IsSuccess)
                return itemResult.Error!;
        }

        var lockResult = StorageLocationLock.CreateForInventoryCount(
            location.Id,
            inventoryCount.Id,
            $"инвентаризация {inventoryCount.Number}",
            now,
            userId);
        if (!lockResult.IsSuccess)
            return lockResult.Error!;

        location.AdvanceOperationalRevision();
        dbContext.InventoryCounts.Add(inventoryCount);
        dbContext.StorageLocationLocks.Add(lockResult.Value!);
        return inventoryCount;
    }

    public async Task<OperationResult<InventoryCountItem>> IncrementSkuAsync(
        Guid inventoryCountId,
        Guid stockKeepingUnitId,
        string userId,
        CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        var result = await StageIncrementSkuAsync(
            dbContext,
            inventoryCountId,
            stockKeepingUnitId,
            userId,
            ct);
        if (!result.IsSuccess)
            return result.Error!;

        var saveResult = await ApplicationPersistence.SaveChangesAsync(dbContext, ct);
        return saveResult.IsSuccess ? result.Value! : saveResult.Error!;
    }

    internal async Task<OperationResult<InventoryCountItem>> StageIncrementSkuAsync(
        ApplicationDbContext dbContext,
        Guid inventoryCountId,
        Guid stockKeepingUnitId,
        string userId,
        CancellationToken ct)
    {
        var countResult = await LoadDraftAsync(dbContext, inventoryCountId, ct);
        if (!countResult.IsSuccess)
            return countResult.Error!;
        if (!await IsActiveSkuAsync(dbContext, stockKeepingUnitId, ct))
            return OperationError.NotFound($"Номенклатура '{stockKeepingUnitId}' не найдена или недоступна.");

        var inventoryCount = countResult.Value!;
        var itemExists = inventoryCount.Items.Any(
            x => x.StockKeepingUnitId == stockKeepingUnitId);
        var result = inventoryCount.IncrementSku(
            Guid.NewGuid(),
            stockKeepingUnitId,
            DateTimeOffset.UtcNow,
            userId);
        if (result.IsSuccess && !itemExists)
            dbContext.InventoryCountItems.Add(result.Value!);
        return result;
    }

    public async Task<OperationResult> SetCountedQuantityAsync(
        Guid inventoryCountId,
        Guid itemId,
        decimal countedQuantity,
        string userId,
        CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        var result = await StageSetCountedQuantityAsync(
            dbContext,
            inventoryCountId,
            itemId,
            countedQuantity,
            userId,
            ct);
        if (!result.IsSuccess)
            return result;
        return await ApplicationPersistence.SaveChangesAsync(dbContext, ct);
    }

    internal async Task<OperationResult> StageSetCountedQuantityAsync(
        ApplicationDbContext dbContext,
        Guid inventoryCountId,
        Guid itemId,
        decimal countedQuantity,
        string userId,
        CancellationToken ct)
    {
        var countResult = await LoadDraftAsync(dbContext, inventoryCountId, ct);
        return countResult.IsSuccess
            ? countResult.Value!.SetCountedQuantity(itemId, countedQuantity, DateTimeOffset.UtcNow, userId)
            : countResult.Error!;
    }

    public async Task<OperationResult<InventoryCountItem>> SetSkuCountedQuantityAsync(
        Guid inventoryCountId,
        Guid stockKeepingUnitId,
        decimal countedQuantity,
        string userId,
        CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        var result = await StageSetSkuCountedQuantityAsync(
            dbContext,
            inventoryCountId,
            stockKeepingUnitId,
            countedQuantity,
            userId,
            ct);
        if (!result.IsSuccess)
            return result.Error!;

        var saveResult = await ApplicationPersistence.SaveChangesAsync(dbContext, ct);
        return saveResult.IsSuccess ? result.Value! : saveResult.Error!;
    }

    internal async Task<OperationResult<InventoryCountItem>> StageSetSkuCountedQuantityAsync(
        ApplicationDbContext dbContext,
        Guid inventoryCountId,
        Guid stockKeepingUnitId,
        decimal countedQuantity,
        string userId,
        CancellationToken ct)
    {
        var countResult = await LoadDraftAsync(dbContext, inventoryCountId, ct);
        if (!countResult.IsSuccess)
            return countResult.Error!;
        if (!await IsActiveSkuAsync(dbContext, stockKeepingUnitId, ct))
            return OperationError.NotFound($"Номенклатура '{stockKeepingUnitId}' не найдена или недоступна.");

        var inventoryCount = countResult.Value!;
        var itemExists = inventoryCount.Items.Any(
            x => x.StockKeepingUnitId == stockKeepingUnitId);
        var result = inventoryCount.SetSkuCountedQuantity(
            Guid.NewGuid(),
            stockKeepingUnitId,
            countedQuantity,
            DateTimeOffset.UtcNow,
            userId);
        if (result.IsSuccess && !itemExists)
            dbContext.InventoryCountItems.Add(result.Value!);
        return result;
    }

    public async Task<OperationResult> RemoveUnexpectedItemAsync(
        Guid inventoryCountId,
        Guid itemId,
        string userId,
        CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        var result = await StageRemoveUnexpectedItemAsync(
            dbContext,
            inventoryCountId,
            itemId,
            userId,
            ct);
        if (!result.IsSuccess)
            return result;
        return await ApplicationPersistence.SaveChangesAsync(dbContext, ct);
    }

    internal async Task<OperationResult> StageRemoveUnexpectedItemAsync(
        ApplicationDbContext dbContext,
        Guid inventoryCountId,
        Guid itemId,
        string userId,
        CancellationToken ct)
    {
        var countResult = await LoadDraftAsync(dbContext, inventoryCountId, ct);
        if (!countResult.IsSuccess)
            return countResult.Error!;

        var item = countResult.Value!.Items.SingleOrDefault(x => x.Id == itemId);
        var result = countResult.Value.RemoveUnexpectedItem(itemId, DateTimeOffset.UtcNow, userId);
        if (result.IsSuccess && item is not null)
            dbContext.InventoryCountItems.Remove(item);
        return result;
    }

    public async Task<OperationResult> DeleteDraftAsync(
        Guid inventoryCountId,
        string userId,
        CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        var result = await StageDeleteDraftAsync(dbContext, inventoryCountId, userId, ct);
        if (!result.IsSuccess)
            return result;
        return await ApplicationPersistence.SaveChangesAsync(dbContext, ct);
    }

    internal async Task<OperationResult> StageDeleteDraftAsync(
        ApplicationDbContext dbContext,
        Guid inventoryCountId,
        string userId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return OperationError.Invalid("Пользователь операции не определён.");

        var countResult = await LoadDraftAsync(dbContext, inventoryCountId, ct);
        if (!countResult.IsSuccess)
            return countResult.Error!;

        var inventoryCount = countResult.Value!;
        var location = inventoryCount.StorageLocation!;
        location.AdvanceOperationalRevision();
        dbContext.StorageLocationLocks.Remove(location.ActiveLock!);
        dbContext.InventoryCounts.Remove(inventoryCount);
        return OperationResult.Success();
    }

    public async Task<OperationResult> PostAsync(
        Guid inventoryCountId,
        string userId,
        CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        var result = await StagePostAsync(dbContext, inventoryCountId, userId, ct);
        if (!result.IsSuccess)
            return result;
        return await ApplicationPersistence.SaveChangesAsync(dbContext, ct);
    }

    internal async Task<OperationResult> StagePostAsync(
        ApplicationDbContext dbContext,
        Guid inventoryCountId,
        string userId,
        CancellationToken ct)
    {
        var countResult = await LoadDraftAsync(dbContext, inventoryCountId, ct);
        if (!countResult.IsSuccess)
            return countResult.Error!;

        var inventoryCount = countResult.Value!;
        var expectedResult = await ValidateExpectedBalancesAsync(dbContext, inventoryCount, ct);
        if (!expectedResult.IsSuccess)
            return expectedResult;

        var now = DateTimeOffset.UtcNow;
        var postResult = inventoryCount.Post(now, userId);
        if (!postResult.IsSuccess)
            return postResult;

        var movementsResult = CreateDifferenceMovements(inventoryCount, now, userId);
        if (!movementsResult.IsSuccess)
            return movementsResult.Error!;

        var movements = movementsResult.Value!;
        dbContext.InventoryMovements.AddRange(movements);
        if (movements.Count > 0)
        {
            var postingResult = await inventoryPostingService.PostInventoryMovementsAsync(movements, dbContext, ct);
            if (!postingResult.IsSuccess)
                return postingResult;
        }
        else
        {
            inventoryCount.StorageLocation!.AdvanceOperationalRevision();
        }

        dbContext.StorageLocationLocks.Remove(inventoryCount.StorageLocation!.ActiveLock!);
        return OperationResult.Success();
    }

    private static async Task<OperationResult<InventoryCount>> LoadDraftAsync(
        ApplicationDbContext dbContext,
        Guid inventoryCountId,
        CancellationToken ct)
    {
        var inventoryCount = await dbContext.InventoryCounts
            .Include(x => x.Items)
            .Include(x => x.StorageLocation)
                .ThenInclude(x => x!.ActiveLock)
            .SingleOrDefaultAsync(x => x.Id == inventoryCountId, ct);
        if (inventoryCount is null)
            return OperationError.NotFound($"Инвентаризация '{inventoryCountId}' не найдена.");
        if (inventoryCount.Status != InventoryCountStatus.Draft)
            return OperationError.Invalid("Изменять можно только черновик инвентаризации.");
        if (inventoryCount.StorageLocation?.ActiveLock is not StorageLocationLock locationLock
            || locationLock.OwnerType != StorageLocationLockOwnerType.InventoryCount
            || locationLock.OwnerId != inventoryCount.Id)
            return OperationError.Conflict("Ячейка больше не заблокирована этой инвентаризацией.");
        return inventoryCount;
    }

    private static Task<bool> IsActiveSkuAsync(
        ApplicationDbContext dbContext,
        Guid stockKeepingUnitId,
        CancellationToken ct) =>
        dbContext.StockKeepingUnits.AnyAsync(
            x => x.Id == stockKeepingUnitId && !x.DeletionMark,
            ct);

    private static async Task<OperationResult> ValidateExpectedBalancesAsync(
        ApplicationDbContext dbContext,
        InventoryCount inventoryCount,
        CancellationToken ct)
    {
        var currentBalances = await dbContext.InventoryBalances
            .AsNoTracking()
            .Where(x => x.WarehouseId == inventoryCount.WarehouseId
                && x.StorageLocationId == inventoryCount.StorageLocationId
                && x.Quantity > 0)
            .ToDictionaryAsync(x => x.StockKeepingUnitId, x => x.Quantity, ct);
        var expectedItems = inventoryCount.Items
            .Where(x => x.IsExpected)
            .ToDictionary(x => x.StockKeepingUnitId, x => x.ExpectedQuantity);

        return currentBalances.Count == expectedItems.Count
            && currentBalances.All(x => expectedItems.TryGetValue(x.Key, out var quantity)
                && quantity == x.Value)
            ? OperationResult.Success()
            : OperationError.Conflict("Остатки ячейки изменились после начала инвентаризации. Обновите данные.");
    }

    private static OperationResult<List<InventoryMovement>> CreateDifferenceMovements(
        InventoryCount inventoryCount,
        DateTimeOffset createdAtUtc,
        string confirmedBy)
    {
        var movements = new List<InventoryMovement>();
        foreach (var item in inventoryCount.Items.Where(x => x.DifferenceQuantity != 0))
        {
            var difference = item.DifferenceQuantity!.Value;
            var movementResult = InventoryMovement.Create(
                Guid.NewGuid(),
                inventoryCount.WarehouseId,
                difference < 0 ? inventoryCount.StorageLocationId : null,
                difference > 0 ? inventoryCount.StorageLocationId : null,
                item.StockKeepingUnitId,
                Math.Abs(difference),
                createdAtUtc,
                RecorderType.InventoryCount,
                inventoryCount.Id,
                item.LineNumber,
                confirmedBy);
            if (!movementResult.IsSuccess)
                return movementResult.Error!;
            movements.Add(movementResult.Value!);
        }
        return movements;
    }

    private static string GetAddress(StorageLocation location) =>
        location.Zone is null ? location.Code : $"{location.Zone.Code}-{location.Code}";
}

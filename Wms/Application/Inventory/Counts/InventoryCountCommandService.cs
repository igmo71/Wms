using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Wms.Application.Commands;
using Wms.Application.Inventory.Movements;
using Wms.Application.StockKeepingUnits;
using Wms.Common;
using Wms.Data;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Application.Inventory.Counts;

public sealed class InventoryCountCommandService(
    CommandExecutor commandExecutor,
    InventoryPostingService inventoryPostingService,
    StockKeepingUnitService stockKeepingUnitService)
{
    // Keep the persisted command type stable for receipts created by earlier versions.
    private const string StartCommand = "inventory-count.create";
    private const string IncrementCommand = "inventory-count.increment-sku";
    private const string SetQuantityCommand = "inventory-count.set-quantity";
    private const string SetSkuQuantityCommand = "inventory-count.set-sku-quantity";
    private const string RemoveItemCommand = "inventory-count.remove-item";
    private const string PostCommand = "inventory-count.post";
    private const string DeleteDraftCommand = "inventory-count.delete-draft";

    public Task<OperationResult<Guid>> StartAsync(
        StartInventoryCountCommand command,
        CommandContext context,
        CancellationToken ct = default) =>
        commandExecutor.ExecuteAsync(
            StartCommand,
            context.RequestId,
            Hash(command.WarehouseId, command.StorageLocationId),
            context.UserId,
            async (dbContext, token) =>
            {
                var existing = await dbContext.InventoryCounts
                    .AsNoTracking()
                    .Where(x => x.StorageLocationId == command.StorageLocationId
                        && x.Status == InventoryCountStatus.Draft)
                    .Select(x => new { x.Id, x.WarehouseId })
                    .SingleOrDefaultAsync(token);
                if (existing is not null)
                {
                    return existing.WarehouseId == command.WarehouseId
                        ? existing.Id
                        : OperationError.Invalid("Ячейка принадлежит другому складу.");
                }

                var result = await CreateCoreAsync(
                    dbContext,
                    command.WarehouseId,
                    command.StorageLocationId,
                    context.UserId,
                    token);
                return result.IsSuccess ? result.Value!.Id : result.Error!;
            },
            ct);

    public Task<OperationResult<Guid>> IncrementSkuAsync(
        IncrementInventoryCountSkuCommand command,
        CommandContext context,
        CancellationToken ct = default) =>
        commandExecutor.ExecuteAsync(
            IncrementCommand,
            context.RequestId,
            CommandExecutor.ComputeHash($"{command.InventoryCountId:N}|{command.Barcode}"),
            context.UserId,
            async (dbContext, token) =>
            {
                var skuResult = await stockKeepingUnitService.ResolveByBarcodeAsync(
                    dbContext,
                    command.Barcode,
                    token);
                if (!skuResult.IsSuccess)
                    return skuResult.Error!;

                var result = await IncrementSkuCoreAsync(
                    dbContext,
                    command.InventoryCountId,
                    skuResult.Value!.Id,
                    context.UserId,
                    token);
                return result.IsSuccess ? result.Value!.Id : result.Error!;
            },
            ct);

    public Task<OperationResult<Guid>> SetCountedQuantityAsync(
        SetInventoryCountQuantityCommand command,
        CommandContext context,
        CancellationToken ct = default) =>
        commandExecutor.ExecuteAsync(
            SetQuantityCommand,
            context.RequestId,
            CommandExecutor.ComputeHash(string.Join(
                '|',
                command.InventoryCountId.ToString("N"),
                command.ItemId.ToString("N"),
                command.CountedQuantity.ToString("G29", CultureInfo.InvariantCulture))),
            context.UserId,
            async (dbContext, token) =>
            {
                var result = await SetCountedQuantityCoreAsync(
                    dbContext,
                    command.InventoryCountId,
                    command.ItemId,
                    command.CountedQuantity,
                    context.UserId,
                    token);
                return result.IsSuccess ? command.ItemId : result.Error!;
            },
            ct);

    public Task<OperationResult<Guid>> RemoveUnexpectedItemAsync(
        RemoveInventoryCountItemCommand command,
        CommandContext context,
        CancellationToken ct = default) =>
        ExecuteDocumentActionAsync(
            RemoveItemCommand,
            command.InventoryCountId,
            command.ItemId,
            context,
            (dbContext, token) => RemoveUnexpectedItemCoreAsync(
                dbContext,
                command.InventoryCountId,
                command.ItemId,
                context.UserId,
                token),
            ct);

    public Task<OperationResult<Guid>> SetSkuCountedQuantityAsync(
        SetInventoryCountSkuQuantityCommand command,
        CommandContext context,
        CancellationToken ct = default) =>
        commandExecutor.ExecuteAsync(
            SetSkuQuantityCommand,
            context.RequestId,
            CommandExecutor.ComputeHash(string.Join(
                '|',
                command.InventoryCountId.ToString("N"),
                command.StockKeepingUnitId.ToString("N"),
                command.CountedQuantity.ToString("G29", CultureInfo.InvariantCulture))),
            context.UserId,
            async (dbContext, token) =>
            {
                var result = await SetSkuCountedQuantityCoreAsync(
                    dbContext,
                    command.InventoryCountId,
                    command.StockKeepingUnitId,
                    command.CountedQuantity,
                    context.UserId,
                    token);
                return result.IsSuccess ? result.Value!.Id : result.Error!;
            },
            ct);

    public Task<OperationResult<Guid>> PostAsync(
        Guid inventoryCountId,
        CommandContext context,
        CancellationToken ct = default) =>
        ExecuteDocumentActionAsync(
            PostCommand,
            inventoryCountId,
            null,
            context,
            (dbContext, token) => PostCoreAsync(
                dbContext,
                inventoryCountId,
                context.UserId,
                token),
            ct);

    public Task<OperationResult<Guid>> DeleteDraftAsync(
        Guid inventoryCountId,
        CommandContext context,
        CancellationToken ct = default) =>
        ExecuteDocumentActionAsync(
            DeleteDraftCommand,
            inventoryCountId,
            null,
            context,
            (dbContext, token) => DeleteDraftCoreAsync(
                dbContext,
                inventoryCountId,
                context.UserId,
                token),
            ct);

    private Task<OperationResult<Guid>> ExecuteDocumentActionAsync(
        string commandType,
        Guid inventoryCountId,
        Guid? itemId,
        CommandContext context,
        Func<ApplicationDbContext, CancellationToken, Task<OperationResult>> action,
        CancellationToken ct) =>
        commandExecutor.ExecuteAsync(
            commandType,
            context.RequestId,
            itemId is Guid id ? Hash(inventoryCountId, id) : Hash(inventoryCountId),
            context.UserId,
            async (dbContext, token) =>
            {
                var result = await action(dbContext, token);
                return result.IsSuccess
                    ? itemId ?? inventoryCountId
                    : result.Error!;
            },
            ct);

    private static string Hash(params Guid[] ids) =>
        CommandExecutor.ComputeHash(string.Join('|', ids.Select(x => x.ToString("N"))));

    private async Task<OperationResult<InventoryCount>> CreateCoreAsync(
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
        var locationResult = InventoryCountLocationPolicy.RequireActiveStorageLocation(
            location,
            warehouseId);
        if (!locationResult.IsSuccess)
            return locationResult.Error!;
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

    private async Task<OperationResult<InventoryCountItem>> IncrementSkuCoreAsync(
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
            return OperationError.NotFound($"SKU '{stockKeepingUnitId}' не найден или недоступен.");

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

    private async Task<OperationResult> SetCountedQuantityCoreAsync(
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

    private async Task<OperationResult<InventoryCountItem>> SetSkuCountedQuantityCoreAsync(
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
            return OperationError.NotFound($"SKU '{stockKeepingUnitId}' не найден или недоступен.");

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

    private async Task<OperationResult> RemoveUnexpectedItemCoreAsync(
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

    private async Task<OperationResult> DeleteDraftCoreAsync(
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

    private async Task<OperationResult> PostCoreAsync(
        ApplicationDbContext dbContext,
        Guid inventoryCountId,
        string userId,
        CancellationToken ct)
    {
        var countResult = await LoadDraftAsync(dbContext, inventoryCountId, ct);
        if (!countResult.IsSuccess)
            return countResult.Error!;

        var inventoryCount = countResult.Value!;
        var locationResult = InventoryCountLocationPolicy.RequireActiveStorageLocation(
            inventoryCount.StorageLocation!,
            inventoryCount.WarehouseId);
        if (!locationResult.IsSuccess)
            return locationResult;

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
            .Include(x => x.StorageLocation)
                .ThenInclude(x => x!.Warehouse)
            .Include(x => x.StorageLocation)
                .ThenInclude(x => x!.Zone)
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
        location.DisplayAddress;
}

using Microsoft.EntityFrameworkCore;
using Wms.Application.Inventory;
using Wms.Application.Inventory.Movements;
using Wms.Application.StorageLocations;
using Wms.Common;
using Wms.Data;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Application.Inventory.Transfers;

public class InventoryTransferCommandService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    InventoryPostingService inventoryPostingService)
{
    public async Task<OperationResult<InventoryTransfer>> CreateAsync(
        Guid warehouseId,
        Guid? transitStorageLocationId,
        string userId,
        CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        var transferResult = await StageCreateAsync(
            dbContext,
            warehouseId,
            transitStorageLocationId,
            userId,
            ct);
        if (!transferResult.IsSuccess)
        {
            return transferResult.Error!;
        }

        await dbContext.SaveChangesAsync(ct);
        return transferResult.Value!;
    }

    internal async Task<OperationResult<InventoryTransfer>> StageCreateAsync(
        ApplicationDbContext dbContext,
        Guid warehouseId,
        Guid? transitStorageLocationId,
        string userId,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var transferResult = InventoryTransfer.Create(
            Guid.NewGuid(),
            now.LocalDateTime.ToString("yyMMdd-HHmmss"),
            now.LocalDateTime.Date,
            warehouseId,
            transitStorageLocationId,
            now,
            userId);
        if (!transferResult.IsSuccess)
        {
            return transferResult.Error!;
        }

        var contextResult = await ValidateCreationContextAsync(
            dbContext,
            warehouseId,
            transitStorageLocationId,
            ct);
        if (!contextResult.IsSuccess)
        {
            return contextResult.Error!;
        }

        var transfer = transferResult.Value!;
        dbContext.InventoryTransfers.Add(transfer);
        return transfer;
    }

    public async Task<OperationResult> DeleteDraftAsync(Guid transferId, CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        var transfer = await dbContext.InventoryTransfers.FirstOrDefaultAsync(x => x.Id == transferId, ct);
        if (transfer is null)
        {
            return OperationError.NotFound($"Перемещение '{transferId}' не найдено.");
        }

        var deletionResult = transfer.ValidateDeletion();
        if (!deletionResult.IsSuccess)
        {
            return deletionResult;
        }

        dbContext.InventoryTransfers.Remove(transfer);
        return await SaveChangesAsync(dbContext, ct);
    }

    public Task<OperationResult> PickAsync(
        Guid transferId,
        Guid sourceStorageLocationId,
        Guid stockKeepingUnitId,
        double quantity,
        string userId,
        CancellationToken ct = default) =>
        PostMovementAsync(
            transferId,
            MovementMode.Pick,
            sourceStorageLocationId,
            null,
            stockKeepingUnitId,
            quantity,
            userId,
            ct);

    public Task<OperationResult> PutAsync(
        Guid transferId,
        Guid destinationStorageLocationId,
        Guid stockKeepingUnitId,
        double quantity,
        string userId,
        CancellationToken ct = default) =>
        PostMovementAsync(
            transferId,
            MovementMode.Put,
            null,
            destinationStorageLocationId,
            stockKeepingUnitId,
            quantity,
            userId,
            ct);

    public Task<OperationResult> MoveDirectAsync(
        Guid transferId,
        Guid sourceStorageLocationId,
        Guid destinationStorageLocationId,
        Guid stockKeepingUnitId,
        double quantity,
        string userId,
        CancellationToken ct = default) =>
        PostMovementAsync(
            transferId,
            MovementMode.Direct,
            sourceStorageLocationId,
            destinationStorageLocationId,
            stockKeepingUnitId,
            quantity,
            userId,
            ct);

    internal Task<OperationResult<InventoryMovement>> StageDirectMovementAsync(
        ApplicationDbContext dbContext,
        Guid transferId,
        Guid sourceStorageLocationId,
        Guid destinationStorageLocationId,
        Guid stockKeepingUnitId,
        double quantity,
        string userId,
        CancellationToken ct) =>
        StageMovementAsync(
            dbContext,
            transferId,
            MovementMode.Direct,
            sourceStorageLocationId,
            destinationStorageLocationId,
            stockKeepingUnitId,
            quantity,
            userId,
            ct);

    internal Task<OperationResult<InventoryMovement>> StagePickMovementAsync(
        ApplicationDbContext dbContext,
        Guid transferId,
        Guid sourceStorageLocationId,
        Guid stockKeepingUnitId,
        double quantity,
        string userId,
        CancellationToken ct) =>
        StageMovementAsync(
            dbContext,
            transferId,
            MovementMode.Pick,
            sourceStorageLocationId,
            null,
            stockKeepingUnitId,
            quantity,
            userId,
            ct);

    internal Task<OperationResult<InventoryMovement>> StagePutMovementAsync(
        ApplicationDbContext dbContext,
        Guid transferId,
        Guid destinationStorageLocationId,
        Guid stockKeepingUnitId,
        double quantity,
        string userId,
        CancellationToken ct) =>
        StageMovementAsync(
            dbContext,
            transferId,
            MovementMode.Put,
            null,
            destinationStorageLocationId,
            stockKeepingUnitId,
            quantity,
            userId,
            ct);

    public async Task<OperationResult> CompleteAsync(
        Guid transferId,
        string userId,
        CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        var completionResult = await StageCompleteAsync(dbContext, transferId, userId, ct);
        if (!completionResult.IsSuccess)
        {
            return completionResult.Error!;
        }

        return await SaveChangesAsync(dbContext, ct);
    }

    internal async Task<OperationResult<InventoryTransfer>> StageCompleteAsync(
        ApplicationDbContext dbContext,
        Guid transferId,
        string userId,
        CancellationToken ct)
    {
        var transfer = await dbContext.InventoryTransfers.FirstOrDefaultAsync(x => x.Id == transferId, ct);
        if (transfer is null)
        {
            return OperationError.NotFound($"Перемещение '{transferId}' не найдено.");
        }

        if (transfer.TransitStorageLocationId is Guid transitLocationId
            && await dbContext.InventoryBalances.AnyAsync(
                x => x.StorageLocationId == transitLocationId && x.Quantity > 0,
                ct))
        {
            return OperationError.Invalid(
                "Перед завершением перемещения транзитная позиция должна быть пустой.");
        }

        var now = DateTimeOffset.UtcNow;
        var completionResult = transfer.Complete(now, userId);
        if (!completionResult.IsSuccess)
        {
            return completionResult.Error!;
        }

        return transfer;
    }

    private async Task<OperationResult> PostMovementAsync(
        Guid transferId,
        MovementMode mode,
        Guid? enteredSourceStorageLocationId,
        Guid? enteredDestinationStorageLocationId,
        Guid stockKeepingUnitId,
        double quantity,
        string userId,
        CancellationToken ct)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        var movementResult = await StageMovementAsync(
            dbContext,
            transferId,
            mode,
            enteredSourceStorageLocationId,
            enteredDestinationStorageLocationId,
            stockKeepingUnitId,
            quantity,
            userId,
            ct);
        if (!movementResult.IsSuccess)
        {
            return movementResult.Error!;
        }

        return await SaveChangesAsync(dbContext, ct);
    }

    private async Task<OperationResult<InventoryMovement>> StageMovementAsync(
        ApplicationDbContext dbContext,
        Guid transferId,
        MovementMode mode,
        Guid? enteredSourceStorageLocationId,
        Guid? enteredDestinationStorageLocationId,
        Guid stockKeepingUnitId,
        double quantity,
        string userId,
        CancellationToken ct)
    {
        if (!double.IsFinite(quantity) || quantity <= 0)
        {
            return OperationError.Invalid(
                "Количество движения должно быть конечным числом больше нуля.");
        }

        var transfer = await dbContext.InventoryTransfers.FirstOrDefaultAsync(x => x.Id == transferId, ct);
        if (transfer is null)
        {
            return OperationError.NotFound($"Перемещение '{transferId}' не найдено.");
        }

        var routeResult = CreateRoute(
            transfer,
            mode,
            enteredSourceStorageLocationId,
            enteredDestinationStorageLocationId);
        if (!routeResult.IsSuccess)
        {
            return routeResult.Error!;
        }

        if (!await dbContext.StockKeepingUnits.AnyAsync(
            x => x.Id == stockKeepingUnitId && !x.DeletionMark,
            ct))
        {
            return OperationError.NotFound(
                $"Активная номенклатура '{stockKeepingUnitId}' не найдена.");
        }

        var route = routeResult.Value!;
        var locationsResult = await ValidateLocationsAsync(dbContext, transfer, route, mode, ct);
        if (!locationsResult.IsSuccess)
        {
            return locationsResult.Error!;
        }

        var lineNumber = (await dbContext.InventoryMovements
            .Where(x => x.RecorderType == RecorderType.InventoryTransfer && x.RecorderId == transfer.Id)
            .Select(x => (int?)x.RecorderLineNumber)
            .MaxAsync(ct) ?? 0) + 1;

        var now = DateTimeOffset.UtcNow;
        var movementResult = transfer.RecordMovement(now, userId);
        if (!movementResult.IsSuccess)
        {
            return movementResult.Error!;
        }

        var inventoryMovementResult = InventoryMovement.Create(
            Guid.NewGuid(),
            transfer.WarehouseId,
            route.SourceStorageLocationId,
            route.DestinationStorageLocationId,
            stockKeepingUnitId,
            quantity,
            now,
            RecorderType.InventoryTransfer,
            transfer.Id,
            lineNumber,
            userId);
        if (!inventoryMovementResult.IsSuccess)
        {
            return inventoryMovementResult.Error!;
        }

        var movement = inventoryMovementResult.Value!;
        dbContext.InventoryMovements.Add(movement);
        var postingResult = await inventoryPostingService.PostInventoryMovementsAsync(
            [movement],
            dbContext,
            ct);
        if (!postingResult.IsSuccess)
        {
            return postingResult.Error!;
        }

        return movement;
    }

    private static async Task<OperationResult> SaveChangesAsync(
        ApplicationDbContext dbContext,
        CancellationToken ct) =>
        await InventoryPersistence.SaveChangesAsync(dbContext, ct);

    private static OperationResult<InventoryTransferRoute> CreateRoute(
        InventoryTransfer transfer,
        MovementMode mode,
        Guid? enteredSourceStorageLocationId,
        Guid? enteredDestinationStorageLocationId)
    {
        return mode switch
        {
            MovementMode.Pick => transfer.CreatePickRoute(enteredSourceStorageLocationId ?? Guid.Empty),
            MovementMode.Put => transfer.CreatePutRoute(enteredDestinationStorageLocationId ?? Guid.Empty),
            MovementMode.Direct => transfer.CreateDirectRoute(
                enteredSourceStorageLocationId ?? Guid.Empty,
                enteredDestinationStorageLocationId ?? Guid.Empty),
            _ => OperationError.Invalid("Указан некорректный режим движения перемещения.")
        };
    }

    private static async Task<OperationResult> ValidateCreationContextAsync(
        ApplicationDbContext dbContext,
        Guid warehouseId,
        Guid? transitStorageLocationId,
        CancellationToken ct)
    {
        if (!await dbContext.Warehouses.AnyAsync(
            x => x.Id == warehouseId && !x.DeletionMark,
            ct))
        {
            return OperationError.NotFound($"Активный склад '{warehouseId}' не найден.");
        }

        return transitStorageLocationId is Guid locationId
            ? await ValidateTransitStorageLocationAsync(dbContext, warehouseId, locationId, ct)
            : OperationResult.Success();
    }

    private static async Task<OperationResult> ValidateTransitStorageLocationAsync(
        ApplicationDbContext dbContext,
        Guid warehouseId,
        Guid locationId,
        CancellationToken ct)
    {
        var transitLocation = await dbContext.StorageLocations
            .Include(x => x.Zone)
            .Include(x => x.ActiveLock)
            .FirstOrDefaultAsync(x => x.Id == locationId, ct);
        if (transitLocation is null)
        {
            return OperationError.NotFound($"Транзитная складская позиция '{locationId}' не найдена.");
        }

        if (transitLocation.IsFolder)
        {
            return OperationError.Invalid(
                "Транзитная позиция должна быть складской позицией, а не папкой.");
        }

        if (transitLocation.DeletionMark
            || transitLocation.WarehouseId != warehouseId
            || transitLocation.Zone?.DeletionMark == true
            || transitLocation.Zone?.Type != ZoneType.Transit)
        {
            return OperationError.Invalid(
                "Транзитная позиция должна быть активной и принадлежать транзитной зоне склада перемещения.");
        }

        var transitAvailabilityResult = StorageLocationAvailability.ValidateUnlocked(transitLocation);
        if (!transitAvailabilityResult.IsSuccess)
        {
            return transitAvailabilityResult;
        }

        if (await dbContext.InventoryBalances.AnyAsync(
            x => x.StorageLocationId == locationId && x.Quantity > 0,
            ct))
        {
            return OperationError.Invalid(
                "Перед назначением транзитная позиция должна быть пустой.");
        }

        if (await dbContext.InventoryTransfers.AnyAsync(
            x => x.TransitStorageLocationId == locationId
                && x.Status != InventoryTransferStatus.Completed,
            ct))
        {
            return OperationError.Invalid(
                "Транзитная позиция уже назначена другому активному перемещению.");
        }

        return OperationResult.Success();
    }

    private static async Task<OperationResult> ValidateLocationsAsync(
        ApplicationDbContext dbContext,
        InventoryTransfer transfer,
        InventoryTransferRoute route,
        MovementMode mode,
        CancellationToken ct)
    {
        var locationIds = new[]
        {
            route.SourceStorageLocationId,
            route.DestinationStorageLocationId
        };

        var locations = await dbContext.StorageLocations
            .Include(x => x.Zone)
            .Include(x => x.ActiveLock)
            .Where(x => locationIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);

        if (!locations.TryGetValue(route.SourceStorageLocationId, out var sourceLocation)
            || !locations.TryGetValue(route.DestinationStorageLocationId, out var destinationLocation))
        {
            return OperationError.NotFound(
                $"Не найдена складская позиция маршрута перемещения: источник '{route.SourceStorageLocationId}', назначение '{route.DestinationStorageLocationId}'.");
        }

        if (sourceLocation.IsFolder
            || destinationLocation.IsFolder
            || sourceLocation.DeletionMark
            || destinationLocation.DeletionMark
            || sourceLocation.Zone?.DeletionMark == true
            || destinationLocation.Zone?.DeletionMark == true
            || sourceLocation.WarehouseId != transfer.WarehouseId
            || destinationLocation.WarehouseId != transfer.WarehouseId)
        {
            return OperationError.Invalid(
                "Позиции движения должны быть активными и принадлежать складу перемещения.");
        }

        var sourceAvailabilityResult = StorageLocationAvailability.ValidateUnlocked(sourceLocation);
        if (!sourceAvailabilityResult.IsSuccess)
        {
            return sourceAvailabilityResult;
        }

        var destinationAvailabilityResult = StorageLocationAvailability.ValidateUnlocked(destinationLocation);
        if (!destinationAvailabilityResult.IsSuccess)
        {
            return destinationAvailabilityResult;
        }

        var expectedSourceType = mode == MovementMode.Put ? ZoneType.Transit : ZoneType.Storage;
        var expectedDestinationType = mode == MovementMode.Pick ? ZoneType.Transit : ZoneType.Storage;

        return sourceLocation.Zone?.Type == expectedSourceType
            && destinationLocation.Zone?.Type == expectedDestinationType
            ? OperationResult.Success()
            : OperationError.Invalid(
                "Позиции движения не соответствуют выбранному действию перемещения.");
    }

    private enum MovementMode
    {
        Pick,
        Put,
        Direct
    }
}

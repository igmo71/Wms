using Microsoft.EntityFrameworkCore;
using Wms.Common;
using Wms.Data;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Application.Services.Inventory;

public class InventoryTransferCommandService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    BalanceAndTurnoverService balanceAndTurnoverService)
{
    public async Task<OperationResult<InventoryTransfer>> CreateAsync(
        Guid warehouseId,
        Guid? transitStorageLocationId,
        string userId,
        CancellationToken ct = default)
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

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);
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
        await dbContext.SaveChangesAsync(ct);
        return transfer;
    }

    public async Task<OperationResult> DeleteDraftAsync(Guid transferId, CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        var transfer = await dbContext.InventoryTransfers.FirstOrDefaultAsync(x => x.Id == transferId, ct);
        if (transfer is null)
        {
            return OperationError.NotFound<InventoryTransfer>();
        }

        var deletionResult = transfer.ValidateDeletion();
        if (!deletionResult.IsSuccess)
        {
            return deletionResult;
        }

        dbContext.InventoryTransfers.Remove(transfer);
        await dbContext.SaveChangesAsync(ct);
        return OperationResult.Success();
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

    public async Task<OperationResult> CompleteAsync(
        Guid transferId,
        string userId,
        CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        var transfer = await dbContext.InventoryTransfers.FirstOrDefaultAsync(x => x.Id == transferId, ct);
        if (transfer is null)
        {
            return OperationError.NotFound<InventoryTransfer>();
        }

        if (transfer.TransitStorageLocationId is Guid transitLocationId
            && await dbContext.InventoryBalances.AnyAsync(
                x => x.StorageLocationId == transitLocationId && x.Quantity > 0,
                ct))
        {
            return OperationError.Invalid<InventoryTransfer>(
                "Transit location must be empty before completing the inventory transfer.");
        }

        var now = DateTimeOffset.UtcNow;
        var completionResult = transfer.Complete(now, userId);
        if (!completionResult.IsSuccess)
        {
            return completionResult;
        }

        await dbContext.SaveChangesAsync(ct);
        return OperationResult.Success();
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
        if (!double.IsFinite(quantity) || quantity <= 0)
        {
            return OperationError.Invalid<InventoryMovement>(
                "Movement quantity must be a finite number greater than zero.");
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        var transfer = await dbContext.InventoryTransfers.FirstOrDefaultAsync(x => x.Id == transferId, ct);
        if (transfer is null)
        {
            return OperationError.NotFound<InventoryTransfer>();
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
            return OperationError.NotFound<StockKeepingUnit>();
        }

        var route = routeResult.Value!;
        var locationsResult = await ValidateLocationsAsync(dbContext, transfer, route, mode, ct);
        if (!locationsResult.IsSuccess)
        {
            return locationsResult;
        }

        var lineNumber = (await dbContext.InventoryMovements
            .Where(x => x.RecorderType == RecorderType.InventoryTransfer && x.RecorderId == transfer.Id)
            .Select(x => (int?)x.RecorderLineNumber)
            .MaxAsync(ct) ?? 0) + 1;

        var now = DateTimeOffset.UtcNow;
        var movementResult = transfer.RecordMovement(now, userId);
        if (!movementResult.IsSuccess)
        {
            return movementResult;
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
        var postingResult = await balanceAndTurnoverService.PostInventoryMovementsAsync(
            [movement],
            dbContext,
            ct);
        if (!postingResult.IsSuccess)
        {
            return postingResult;
        }

        await dbContext.SaveChangesAsync(ct);
        return OperationResult.Success();
    }

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
            _ => OperationError.Invalid<InventoryMovement>("Transfer movement mode is invalid.")
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
            return OperationError.NotFound<Warehouse>();
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
            .FirstOrDefaultAsync(x => x.Id == locationId, ct);
        if (transitLocation is null)
        {
            return OperationError.NotFound<StorageLocation>();
        }

        if (transitLocation.IsFolder)
        {
            return OperationError.Invalid<StorageLocation>(
                "Transit location must be an inventory location.");
        }

        if (transitLocation.DeletionMark
            || transitLocation.WarehouseId != warehouseId
            || transitLocation.Zone?.DeletionMark == true
            || transitLocation.Zone?.Type != ZoneType.Transit)
        {
            return OperationError.Invalid<StorageLocation>(
                "Transit location must be active and belong to a transit zone in the transfer warehouse.");
        }

        if (await dbContext.InventoryBalances.AnyAsync(
            x => x.StorageLocationId == locationId && x.Quantity > 0,
            ct))
        {
            return OperationError.Invalid<StorageLocation>(
                "Transit location must be empty before assignment.");
        }

        if (await dbContext.InventoryTransfers.AnyAsync(
            x => x.TransitStorageLocationId == locationId
                && x.Status != InventoryTransferStatus.Completed,
            ct))
        {
            return OperationError.Invalid<StorageLocation>(
                "Transit location is already assigned to an active inventory transfer.");
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
            .Where(x => locationIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);

        if (!locations.TryGetValue(route.SourceStorageLocationId, out var sourceLocation)
            || !locations.TryGetValue(route.DestinationStorageLocationId, out var destinationLocation))
        {
            return OperationError.NotFound<StorageLocation>();
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
            return OperationError.Invalid<StorageLocation>(
                "Movement locations must be active and belong to the transfer warehouse.");
        }

        var expectedSourceType = mode == MovementMode.Put ? ZoneType.Transit : ZoneType.Storage;
        var expectedDestinationType = mode == MovementMode.Pick ? ZoneType.Transit : ZoneType.Storage;

        return sourceLocation.Zone?.Type == expectedSourceType
            && destinationLocation.Zone?.Type == expectedDestinationType
            ? OperationResult.Success()
            : OperationError.Invalid<StorageLocation>(
                "Movement locations do not match the selected transfer action.");
    }

    private enum MovementMode
    {
        Pick,
        Put,
        Direct
    }
}

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
    public async Task<ServiceResult<InventoryTransfer>> CreateAsync(
        Guid warehouseId,
        string userId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return ServiceError.Invalid<InventoryTransfer>("Creating user must be specified.");

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        if (!await dbContext.Warehouses.AnyAsync(x => x.Id == warehouseId && !x.DeletionMark, ct))
            return ServiceError.NotFound<Warehouse>();

        var now = DateTimeOffset.UtcNow;
        var transfer = new InventoryTransfer
        {
            Id = Guid.NewGuid(),
            Number = now.LocalDateTime.ToString("yyMMdd-HHmmss"),
            Date = now.LocalDateTime.Date,
            WarehouseId = warehouseId,
            Status = InventoryTransferStatus.Draft,
            CreatedAtUtc = now,
            CreatedBy = userId
        };

        dbContext.InventoryTransfers.Add(transfer);
        await dbContext.SaveChangesAsync(ct);

        return transfer;
    }

    public async Task<ServiceResult> DeleteDraftAsync(Guid transferId, CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var transfer = await dbContext.InventoryTransfers.FirstOrDefaultAsync(x => x.Id == transferId, ct);
        if (transfer is null)
            return ServiceError.NotFound<InventoryTransfer>();

        if (transfer.Status != InventoryTransferStatus.Draft)
            return ServiceError.Invalid<InventoryTransfer>("Only a draft inventory transfer can be deleted.");

        dbContext.InventoryTransfers.Remove(transfer);
        await dbContext.SaveChangesAsync(ct);
        return ServiceResult.Success();
    }

    public async Task<ServiceResult> SetTransitStorageLocationAsync(
        Guid transferId,
        Guid? transitStorageLocationId,
        CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var transfer = await dbContext.InventoryTransfers.FirstOrDefaultAsync(x => x.Id == transferId, ct);
        if (transfer is null)
            return ServiceError.NotFound<InventoryTransfer>();

        if (transfer.Status == InventoryTransferStatus.Completed)
            return ServiceError.Invalid<InventoryTransfer>("A completed inventory transfer cannot be changed.");

        if (transfer.TransitStorageLocationId == transitStorageLocationId)
            return ServiceResult.Success();

        if (transfer.TransitStorageLocationId is Guid currentTransitLocationId
            && await dbContext.InventoryMovements.AnyAsync(x => x.RecorderType == RecorderType.InventoryTransfer
                && x.RecorderId == transfer.Id
                && (x.SourceStorageLocationId == currentTransitLocationId
                    || x.DestinationStorageLocationId == currentTransitLocationId), ct))
        {
            return ServiceError.Invalid<InventoryTransfer>("A transit location cannot be changed after it has been used.");
        }

        if (transitStorageLocationId is Guid locationId)
        {
            var transitLocation = await dbContext.StorageLocations
                .Include(x => x.Zone)
                .FirstOrDefaultAsync(x => x.Id == locationId, ct);

            if (transitLocation is null)
                return ServiceError.NotFound<StorageLocation>();

            if (transitLocation.DeletionMark
                || transitLocation.WarehouseId != transfer.WarehouseId
                || transitLocation.Zone?.Type != ZoneType.Transit)
            {
                return ServiceError.Invalid<StorageLocation>(
                    "Transit location must be active and belong to a transit zone in the transfer warehouse.");
            }

            if (await dbContext.InventoryBalances.AnyAsync(x => x.StorageLocationId == locationId && x.Quantity > 0, ct))
                return ServiceError.Invalid<StorageLocation>("Transit location must be empty before assignment.");

            if (await dbContext.InventoryTransfers.AnyAsync(x => x.Id != transfer.Id
                && x.TransitStorageLocationId == locationId
                && x.Status != InventoryTransferStatus.Completed, ct))
            {
                return ServiceError.Invalid<StorageLocation>("Transit location is already assigned to an active inventory transfer.");
            }
        }

        transfer.TransitStorageLocationId = transitStorageLocationId;
        transfer.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(ct);
        return ServiceResult.Success();
    }

    public Task<ServiceResult> PickAsync(
        Guid transferId,
        Guid sourceStorageLocationId,
        Guid stockKeepingUnitId,
        double quantity,
        string userId,
        CancellationToken ct = default) =>
        PostMovementAsync(transferId, MovementMode.Pick, sourceStorageLocationId, null,
            stockKeepingUnitId, quantity, userId, ct);

    public Task<ServiceResult> PutAsync(
        Guid transferId,
        Guid destinationStorageLocationId,
        Guid stockKeepingUnitId,
        double quantity,
        string userId,
        CancellationToken ct = default) =>
        PostMovementAsync(transferId, MovementMode.Put, null, destinationStorageLocationId,
            stockKeepingUnitId, quantity, userId, ct);

    public Task<ServiceResult> MoveDirectAsync(
        Guid transferId,
        Guid sourceStorageLocationId,
        Guid destinationStorageLocationId,
        Guid stockKeepingUnitId,
        double quantity,
        string userId,
        CancellationToken ct = default) =>
        PostMovementAsync(transferId, MovementMode.Direct, sourceStorageLocationId,
            destinationStorageLocationId, stockKeepingUnitId, quantity, userId, ct);

    public async Task<ServiceResult> CompleteAsync(
        Guid transferId,
        string userId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return ServiceError.Invalid<InventoryTransfer>("Completing user must be specified.");

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var transfer = await dbContext.InventoryTransfers.FirstOrDefaultAsync(x => x.Id == transferId, ct);
        if (transfer is null)
            return ServiceError.NotFound<InventoryTransfer>();

        if (transfer.Status != InventoryTransferStatus.InProgress)
            return ServiceError.Invalid<InventoryTransfer>("Only an inventory transfer in progress can be completed.");

        if (transfer.TransitStorageLocationId is Guid transitLocationId
            && await dbContext.InventoryBalances.AnyAsync(x => x.StorageLocationId == transitLocationId && x.Quantity > 0, ct))
        {
            return ServiceError.Invalid<InventoryTransfer>("Transit location must be empty before completing the inventory transfer.");
        }

        var now = DateTimeOffset.UtcNow;
        transfer.Status = InventoryTransferStatus.Completed;
        transfer.UpdatedAtUtc = now;
        transfer.CompletedAtUtc = now;
        transfer.CompletedBy = userId;

        await dbContext.SaveChangesAsync(ct);
        return ServiceResult.Success();
    }

    private async Task<ServiceResult> PostMovementAsync(
        Guid transferId,
        MovementMode mode,
        Guid? enteredSourceStorageLocationId,
        Guid? enteredDestinationStorageLocationId,
        Guid stockKeepingUnitId,
        double quantity,
        string userId,
        CancellationToken ct)
    {
        if (quantity <= 0)
            return ServiceError.Invalid<InventoryMovement>("Movement quantity must be greater than zero.");

        if (string.IsNullOrWhiteSpace(userId))
            return ServiceError.Invalid<InventoryMovement>("Confirming user must be specified.");

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var transfer = await dbContext.InventoryTransfers.FirstOrDefaultAsync(x => x.Id == transferId, ct);
        if (transfer is null)
            return ServiceError.NotFound<InventoryTransfer>();

        if (transfer.Status == InventoryTransferStatus.Completed)
            return ServiceError.Invalid<InventoryTransfer>("A completed inventory transfer cannot be changed.");

        if (!await dbContext.StockKeepingUnits.AnyAsync(x => x.Id == stockKeepingUnitId && !x.DeletionMark, ct))
            return ServiceError.NotFound<StockKeepingUnit>();

        Guid? sourceStorageLocationId;
        Guid? destinationStorageLocationId;

        switch (mode)
        {
            case MovementMode.Pick:
                if (transfer.TransitStorageLocationId is null)
                    return ServiceError.Invalid<InventoryTransfer>("Transit location must be assigned before picking.");
                sourceStorageLocationId = enteredSourceStorageLocationId;
                destinationStorageLocationId = transfer.TransitStorageLocationId;
                break;
            case MovementMode.Put:
                if (transfer.TransitStorageLocationId is null)
                    return ServiceError.Invalid<InventoryTransfer>("Transit location must be assigned before putting.");
                sourceStorageLocationId = transfer.TransitStorageLocationId;
                destinationStorageLocationId = enteredDestinationStorageLocationId;
                break;
            case MovementMode.Direct:
                sourceStorageLocationId = enteredSourceStorageLocationId;
                destinationStorageLocationId = enteredDestinationStorageLocationId;
                break;
            default:
                return ServiceError.Invalid<InventoryMovement>("Transfer movement mode is invalid.");
        }

        if (sourceStorageLocationId is null || destinationStorageLocationId is null)
            return ServiceError.Invalid<InventoryMovement>("Source and destination locations must be specified.");

        if (sourceStorageLocationId == destinationStorageLocationId)
            return ServiceError.Invalid<InventoryMovement>("Source and destination locations must be different.");

        var locationIds = new[] { sourceStorageLocationId.Value, destinationStorageLocationId.Value };
        var locations = await dbContext.StorageLocations
            .Include(x => x.Zone)
            .Where(x => locationIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);

        if (!locations.TryGetValue(sourceStorageLocationId.Value, out var sourceLocation)
            || !locations.TryGetValue(destinationStorageLocationId.Value, out var destinationLocation))
        {
            return ServiceError.NotFound<StorageLocation>();
        }

        if (sourceLocation.DeletionMark || destinationLocation.DeletionMark
            || sourceLocation.WarehouseId != transfer.WarehouseId
            || destinationLocation.WarehouseId != transfer.WarehouseId)
        {
            return ServiceError.Invalid<StorageLocation>("Movement locations must be active and belong to the transfer warehouse.");
        }

        var expectedSourceType = mode == MovementMode.Put ? ZoneType.Transit : ZoneType.Storage;
        var expectedDestinationType = mode == MovementMode.Pick ? ZoneType.Transit : ZoneType.Storage;

        if (sourceLocation.Zone?.Type != expectedSourceType || destinationLocation.Zone?.Type != expectedDestinationType)
            return ServiceError.Invalid<StorageLocation>("Movement locations do not match the selected transfer action.");

        var lineNumber = (await dbContext.InventoryMovements
            .Where(x => x.RecorderType == RecorderType.InventoryTransfer && x.RecorderId == transfer.Id)
            .Select(x => (int?)x.RecorderLineNumber)
            .MaxAsync(ct) ?? 0) + 1;
        var now = DateTimeOffset.UtcNow;
        var movement = new InventoryMovement
        {
            Id = Guid.NewGuid(),
            WarehouseId = transfer.WarehouseId,
            SourceStorageLocationId = sourceStorageLocationId,
            DestinationStorageLocationId = destinationStorageLocationId,
            StockKeepingUnitId = stockKeepingUnitId,
            Quantity = quantity,
            CreatedAtUtc = now,
            ConfirmedBy = userId,
            RecorderType = RecorderType.InventoryTransfer,
            RecorderId = transfer.Id,
            RecorderLineNumber = lineNumber
        };

        dbContext.InventoryMovements.Add(movement);
        var postingResult = await balanceAndTurnoverService.PostInventoryMovementsAsync([movement], dbContext, ct);
        if (!postingResult.IsSuccess)
            return postingResult;

        if (transfer.Status == InventoryTransferStatus.Draft)
        {
            transfer.Status = InventoryTransferStatus.InProgress;
            transfer.StartedAtUtc = now;
            transfer.StartedBy = userId;
        }

        transfer.UpdatedAtUtc = now;

        await dbContext.SaveChangesAsync(ct);
        return ServiceResult.Success();
    }

    private enum MovementMode
    {
        Pick,
        Put,
        Direct
    }
}

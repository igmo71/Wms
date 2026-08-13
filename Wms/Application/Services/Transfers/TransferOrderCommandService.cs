using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wms.Common;
using Wms.Data;
using Wms.Domain;
using Wms.Domain.Enums;
using Wms.Application.Services.Inventory;

namespace Wms.Application.Services.Transfers;

public class TransferOrderCommandService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    BalanceAndTurnoverService balanceAndTurnoverService,
    ILogger<TransferOrderCommandService> logger)
{
    public async Task<ServiceResult<TransferOrder>> CreateAsync(
        Guid warehouseId,
        string userId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return ServiceError.Invalid<TransferOrder>("Creating user must be specified.");

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        if (!await dbContext.Warehouses.AnyAsync(x => x.Id == warehouseId && !x.DeletionMark, ct))
            return ServiceError.NotFound<Warehouse>();

        var sequenceNumber = (await dbContext.TransferOrders
            .Select(x => (long?)x.SequenceNumber)
            .MaxAsync(ct) ?? 0) + 1;
        var now = DateTimeOffset.UtcNow;
        var order = new TransferOrder
        {
            Id = Guid.NewGuid(),
            SequenceNumber = sequenceNumber,
            Number = sequenceNumber.ToString("D9"),
            Date = now.LocalDateTime.Date,
            WarehouseId = warehouseId,
            Status = TransferOrderStatus.Draft,
            CreatedAtUtc = now,
            CreatedBy = userId
        };

        dbContext.TransferOrders.Add(order);

        try
        {
            await dbContext.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return order;
        }
        catch (DbUpdateException exception)
        {
            logger.LogWarning(exception, "Transfer order number allocation conflict");
            return ServiceError.Conflict<TransferOrder>("Transfer order could not be numbered because of a concurrent operation. Try again.");
        }
    }

    public async Task<ServiceResult> DeleteDraftAsync(Guid orderId, CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        var order = await dbContext.TransferOrders.FirstOrDefaultAsync(x => x.Id == orderId, ct);
        if (order is null)
            return ServiceError.NotFound<TransferOrder>();

        if (order.Status != TransferOrderStatus.Draft)
            return ServiceError.Invalid<TransferOrder>("Only a draft transfer order can be deleted.");

        if (await HasMovementsAsync(dbContext, order.Id, ct))
            return ServiceError.Invalid<TransferOrder>("A transfer order with movements cannot be deleted.");

        dbContext.TransferOrders.Remove(order);
        await dbContext.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return ServiceResult.Success();
    }

    public async Task<ServiceResult> SetTransitStorageLocationAsync(
        Guid orderId,
        Guid? transitStorageLocationId,
        CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        var order = await dbContext.TransferOrders.FirstOrDefaultAsync(x => x.Id == orderId, ct);
        if (order is null)
            return ServiceError.NotFound<TransferOrder>();

        if (order.Status == TransferOrderStatus.Completed)
            return ServiceError.Invalid<TransferOrder>("A completed transfer order cannot be changed.");

        if (order.TransitStorageLocationId == transitStorageLocationId)
            return ServiceResult.Success();

        if (order.TransitStorageLocationId is Guid currentTransitLocationId
            && await dbContext.InventoryMovements.AnyAsync(x => x.RecorderType == RecorderType.TransferOrder
                && x.RecorderId == order.Id
                && (x.SourceStorageLocationId == currentTransitLocationId
                    || x.DestinationStorageLocationId == currentTransitLocationId), ct))
        {
            return ServiceError.Invalid<TransferOrder>("A transit location cannot be changed after it has been used.");
        }

        if (transitStorageLocationId is Guid locationId)
        {
            var transitLocation = await dbContext.StorageLocations
                .Include(x => x.Zone)
                .FirstOrDefaultAsync(x => x.Id == locationId, ct);

            if (transitLocation is null)
                return ServiceError.NotFound<StorageLocation>();

            if (transitLocation.DeletionMark
                || transitLocation.WarehouseId != order.WarehouseId
                || transitLocation.Zone?.Type != ZoneType.Transit)
            {
                return ServiceError.Invalid<StorageLocation>(
                    "Transit location must be active and belong to a transit zone in the transfer warehouse.");
            }

            if (await dbContext.InventoryBalances.AnyAsync(x => x.StorageLocationId == locationId && x.Quantity > 0, ct))
                return ServiceError.Invalid<StorageLocation>("Transit location must be empty before assignment.");

            if (await dbContext.TransferOrders.AnyAsync(x => x.Id != order.Id
                && x.TransitStorageLocationId == locationId
                && x.Status != TransferOrderStatus.Completed, ct))
            {
                return ServiceError.Conflict<StorageLocation>("Transit location is already assigned to an active transfer order.");
            }
        }

        order.TransitStorageLocationId = transitStorageLocationId;
        order.UpdatedAtUtc = DateTimeOffset.UtcNow;

        try
        {
            await dbContext.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return ServiceResult.Success();
        }
        catch (DbUpdateConcurrencyException exception)
        {
            logger.LogWarning(exception, "Transfer order concurrency conflict {OrderId}", orderId);
            return ServiceError.Conflict<TransferOrder>("Transfer order was changed by another operator. Reload and try again.");
        }
        catch (DbUpdateException exception)
        {
            logger.LogWarning(exception, "Transit location assignment conflict for transfer order {OrderId}", orderId);
            return ServiceError.Conflict<StorageLocation>("Transit location is already assigned to an active transfer order.");
        }
    }

    public Task<ServiceResult> PickAsync(
        Guid orderId,
        Guid sourceStorageLocationId,
        Guid stockKeepingUnitId,
        double quantity,
        string userId,
        CancellationToken ct = default) =>
        PostMovementAsync(orderId, TransferMovementMode.Pick, sourceStorageLocationId, null,
            stockKeepingUnitId, quantity, userId, ct);

    public Task<ServiceResult> PutAsync(
        Guid orderId,
        Guid destinationStorageLocationId,
        Guid stockKeepingUnitId,
        double quantity,
        string userId,
        CancellationToken ct = default) =>
        PostMovementAsync(orderId, TransferMovementMode.Put, null, destinationStorageLocationId,
            stockKeepingUnitId, quantity, userId, ct);

    public Task<ServiceResult> MoveDirectAsync(
        Guid orderId,
        Guid sourceStorageLocationId,
        Guid destinationStorageLocationId,
        Guid stockKeepingUnitId,
        double quantity,
        string userId,
        CancellationToken ct = default) =>
        PostMovementAsync(orderId, TransferMovementMode.Direct, sourceStorageLocationId,
            destinationStorageLocationId, stockKeepingUnitId, quantity, userId, ct);

    public async Task<ServiceResult> CompleteAsync(
        Guid orderId,
        string userId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return ServiceError.Invalid<TransferOrder>("Completing user must be specified.");

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        var order = await dbContext.TransferOrders.FirstOrDefaultAsync(x => x.Id == orderId, ct);
        if (order is null)
            return ServiceError.NotFound<TransferOrder>();

        if (order.Status != TransferOrderStatus.InProgress)
            return ServiceError.Invalid<TransferOrder>("Only a transfer order in progress can be completed.");

        if (!await HasMovementsAsync(dbContext, order.Id, ct))
            return ServiceError.Invalid<TransferOrder>("A transfer order without movements cannot be completed.");

        if (order.TransitStorageLocationId is Guid transitLocationId
            && await dbContext.InventoryBalances.AnyAsync(x => x.StorageLocationId == transitLocationId && x.Quantity > 0, ct))
        {
            return ServiceError.Invalid<TransferOrder>("Transit location must be empty before completing the transfer order.");
        }

        var now = DateTimeOffset.UtcNow;
        order.Status = TransferOrderStatus.Completed;
        order.UpdatedAtUtc = now;
        order.CompletedAtUtc = now;
        order.CompletedBy = userId;

        try
        {
            await dbContext.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return ServiceResult.Success();
        }
        catch (DbUpdateConcurrencyException exception)
        {
            logger.LogWarning(exception, "Transfer order completion conflict {OrderId}", orderId);
            return ServiceError.Conflict<TransferOrder>("Transfer order was changed by another operator. Reload and try again.");
        }
    }

    private async Task<ServiceResult> PostMovementAsync(
        Guid orderId,
        TransferMovementMode mode,
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
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        var order = await dbContext.TransferOrders.FirstOrDefaultAsync(x => x.Id == orderId, ct);
        if (order is null)
            return ServiceError.NotFound<TransferOrder>();

        if (order.Status == TransferOrderStatus.Completed)
            return ServiceError.Invalid<TransferOrder>("A completed transfer order cannot be changed.");

        if (!await dbContext.StockKeepingUnits.AnyAsync(x => x.Id == stockKeepingUnitId && !x.DeletionMark, ct))
            return ServiceError.NotFound<StockKeepingUnit>();

        Guid? sourceStorageLocationId;
        Guid? destinationStorageLocationId;

        switch (mode)
        {
            case TransferMovementMode.Pick:
                if (order.TransitStorageLocationId is null)
                    return ServiceError.Invalid<TransferOrder>("Transit location must be assigned before picking.");
                sourceStorageLocationId = enteredSourceStorageLocationId;
                destinationStorageLocationId = order.TransitStorageLocationId;
                break;
            case TransferMovementMode.Put:
                if (order.TransitStorageLocationId is null)
                    return ServiceError.Invalid<TransferOrder>("Transit location must be assigned before putting.");
                sourceStorageLocationId = order.TransitStorageLocationId;
                destinationStorageLocationId = enteredDestinationStorageLocationId;
                break;
            case TransferMovementMode.Direct:
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
            || sourceLocation.WarehouseId != order.WarehouseId
            || destinationLocation.WarehouseId != order.WarehouseId)
        {
            return ServiceError.Invalid<StorageLocation>("Movement locations must be active and belong to the transfer warehouse.");
        }

        var expectedSourceType = mode == TransferMovementMode.Put ? ZoneType.Transit : ZoneType.Storage;
        var expectedDestinationType = mode == TransferMovementMode.Pick ? ZoneType.Transit : ZoneType.Storage;

        if (sourceLocation.Zone?.Type != expectedSourceType || destinationLocation.Zone?.Type != expectedDestinationType)
            return ServiceError.Invalid<StorageLocation>("Movement locations do not match the selected transfer action.");

        if (mode != TransferMovementMode.Direct
            && (sourceStorageLocationId == order.TransitStorageLocationId
                || destinationStorageLocationId == order.TransitStorageLocationId) == false)
        {
            return ServiceError.Invalid<StorageLocation>("Movement must use the transfer order transit location.");
        }

        var lineNumber = (await dbContext.InventoryMovements
            .Where(x => x.RecorderType == RecorderType.TransferOrder && x.RecorderId == order.Id)
            .Select(x => (int?)x.RecorderLineNumber)
            .MaxAsync(ct) ?? 0) + 1;
        var now = DateTimeOffset.UtcNow;
        var movement = new InventoryMovement
        {
            Id = Guid.NewGuid(),
            WarehouseId = order.WarehouseId,
            SourceStorageLocationId = sourceStorageLocationId,
            DestinationStorageLocationId = destinationStorageLocationId,
            StockKeepingUnitId = stockKeepingUnitId,
            Quantity = quantity,
            CreatedAtUtc = now,
            ConfirmedBy = userId,
            RecorderType = RecorderType.TransferOrder,
            RecorderId = order.Id,
            RecorderLineNumber = lineNumber
        };

        dbContext.InventoryMovements.Add(movement);
        var postingResult = await balanceAndTurnoverService.PostInventoryMovementsAsync([movement], dbContext, ct);
        if (!postingResult.IsSuccess)
            return postingResult;

        if (order.Status == TransferOrderStatus.Draft)
        {
            order.Status = TransferOrderStatus.InProgress;
            order.StartedAtUtc = now;
            order.StartedBy = userId;
        }

        order.UpdatedAtUtc = now;

        try
        {
            await dbContext.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return ServiceResult.Success();
        }
        catch (DbUpdateConcurrencyException exception)
        {
            logger.LogWarning(exception, "Inventory or transfer order concurrency conflict {OrderId}", orderId);
            return ServiceError.Conflict<InventoryMovement>("Inventory changed concurrently. Reload and confirm the physical state before trying again.");
        }
        catch (DbUpdateException exception)
        {
            logger.LogWarning(exception, "Transfer movement persistence conflict {OrderId}", orderId);
            return ServiceError.Conflict<InventoryMovement>("Movement conflicted with another operation. Reload and try again.");
        }
    }

    private static Task<bool> HasMovementsAsync(
        ApplicationDbContext dbContext,
        Guid orderId,
        CancellationToken ct) =>
        dbContext.InventoryMovements.AnyAsync(x => x.RecorderType == RecorderType.TransferOrder
            && x.RecorderId == orderId, ct);

    private enum TransferMovementMode
    {
        Pick,
        Put,
        Direct
    }
}

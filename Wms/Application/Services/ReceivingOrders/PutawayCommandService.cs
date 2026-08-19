using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wms.Application.Services.Inventory;
using Wms.Common;
using Wms.Data;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Application.Services.ReceivingOrders;

public class PutawayCommandService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    BalanceAndTurnoverService balanceAndTurnoverService,
    ILogger<PutawayCommandService> logger)
{
    public async Task<OperationResult> StartAsync(Guid orderId, string userId, CancellationToken ct = default)
    {
        using var scope = logger.BeginScope("Putaway Start {OrderId}", orderId);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var order = await dbContext.ReceivingOrders
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == orderId, ct);

        if (order is null)
        {
            return OperationError.NotFound<ReceivingOrder>();
        }

        var startResult = order.StartPutaway(DateTimeOffset.UtcNow, userId);
        if (!startResult.IsSuccess)
        {
            return startResult;
        }

        await dbContext.SaveChangesAsync(ct);

        return OperationResult.Success();
    }

    public async Task<OperationResult> AddMovementAsync(
        Guid orderId,
        int lineNumber,
        Guid destinationStorageLocationId,
        double quantity,
        CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        var order = await LoadEditableOrderAsync(dbContext, orderId, ct);

        if (order is null)
        {
            return OperationError.NotFound<ReceivingOrder>();
        }

        var draftMovements = await LoadDraftMovementsAsync(dbContext, order.Id, ct);
        var movementResult = order.CreatePutawayMovement(
            Guid.NewGuid(),
            lineNumber,
            destinationStorageLocationId,
            quantity,
            DateTimeOffset.UtcNow,
            draftMovements);
        if (!movementResult.IsSuccess)
        {
            return movementResult.Error!;
        }

        var movement = movementResult.Value!;
        var destinationResult = await ValidateDestinationAsync(
            dbContext, order, destinationStorageLocationId, ct);
        if (!destinationResult.IsSuccess)
        {
            return destinationResult;
        }

        var balanceResult = await ValidateSourceBalanceAsync(
            dbContext, order, movement, draftMovements, null, ct);
        if (!balanceResult.IsSuccess)
        {
            return balanceResult;
        }

        dbContext.InventoryMovements.Add(movement);
        await dbContext.SaveChangesAsync(ct);

        return OperationResult.Success();
    }

    public async Task<OperationResult> UpdateMovementAsync(
        Guid movementId,
        Guid destinationStorageLocationId,
        double quantity,
        CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var movement = await dbContext.InventoryMovements
            .FirstOrDefaultAsync(x => x.Id == movementId, ct);

        if (movement is null)
        {
            return OperationError.NotFound<InventoryMovement>();
        }

        var order = movement.RecorderId is Guid orderId
            ? await LoadEditableOrderAsync(dbContext, orderId, ct)
            : null;
        if (order is null)
        {
            return OperationError.NotFound<ReceivingOrder>();
        }

        var draftMovements = await LoadDraftMovementsAsync(dbContext, order.Id, ct);
        var updateResult = order.UpdatePutawayMovement(
            movement,
            destinationStorageLocationId,
            quantity,
            DateTimeOffset.UtcNow,
            draftMovements);
        if (!updateResult.IsSuccess)
        {
            return updateResult;
        }

        var destinationResult = await ValidateDestinationAsync(
            dbContext, order, destinationStorageLocationId, ct);
        if (!destinationResult.IsSuccess)
        {
            return destinationResult;
        }

        var balanceResult = await ValidateSourceBalanceAsync(
            dbContext, order, movement, draftMovements, movement.Id, ct);
        if (!balanceResult.IsSuccess)
        {
            return balanceResult;
        }

        await dbContext.SaveChangesAsync(ct);
        return OperationResult.Success();
    }

    public async Task<OperationResult> DeleteMovementAsync(Guid movementId, CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var movement = await dbContext.InventoryMovements
            .FirstOrDefaultAsync(x => x.Id == movementId, ct);

        if (movement is null)
        {
            return OperationError.NotFound<InventoryMovement>();
        }

        var order = movement.RecorderId is Guid orderId
            ? await dbContext.ReceivingOrders.FirstOrDefaultAsync(x => x.Id == orderId, ct)
            : null;

        if (order is null)
        {
            return OperationError.NotFound<ReceivingOrder>();
        }

        var removalResult = order.ValidatePutawayMovementRemoval(movement);
        if (!removalResult.IsSuccess)
        {
            return removalResult;
        }

        dbContext.InventoryMovements.Remove(movement);
        await dbContext.SaveChangesAsync(ct);

        return OperationResult.Success();
    }

    public async Task<OperationResult> CompleteAsync(Guid orderId, string userId, CancellationToken ct = default)
    {
        using var scope = logger.BeginScope("Putaway Complete {OrderId}", orderId);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var order = await LoadEditableOrderAsync(dbContext, orderId, ct);
        if (order is null)
        {
            return OperationError.NotFound<ReceivingOrder>();
        }

        var draftMovements = await LoadDraftMovementsAsync(dbContext, order.Id, ct);
        var completionResult = order.CompletePutaway(draftMovements, DateTimeOffset.UtcNow, userId);
        if (!completionResult.IsSuccess)
        {
            return completionResult;
        }

        var destinationsValidation = await ValidateCompletionDestinationsAsync(
            dbContext, order, draftMovements, ct);
        if (!destinationsValidation.IsSuccess)
        {
            return destinationsValidation;
        }

        foreach (var movement in draftMovements)
        {
            var confirmationResult = movement.Confirm(userId);
            if (!confirmationResult.IsSuccess)
            {
                return confirmationResult;
            }
        }

        var postingResult = await balanceAndTurnoverService
            .PostInventoryMovementsAsync(draftMovements, dbContext, ct);
        if (!postingResult.IsSuccess)
        {
            return postingResult;
        }

        await dbContext.SaveChangesAsync(ct);

        return OperationResult.Success();
    }

    private static Task<ReceivingOrder?> LoadEditableOrderAsync(
        ApplicationDbContext dbContext,
        Guid orderId,
        CancellationToken ct) =>
        dbContext.ReceivingOrders
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == orderId, ct);

    private static Task<List<InventoryMovement>> LoadDraftMovementsAsync(
        ApplicationDbContext dbContext,
        Guid orderId,
        CancellationToken ct) =>
        dbContext.InventoryMovements
            .Where(x => x.PostedAtUtc == null
                && x.RecorderType == RecorderType.ReceivingOrder
                && x.RecorderId == orderId)
            .ToListAsync(ct);

    private static async Task<OperationResult> ValidateDestinationAsync(
        ApplicationDbContext dbContext,
        ReceivingOrder order,
        Guid destinationStorageLocationId,
        CancellationToken ct)
    {
        if (destinationStorageLocationId == order.ReceivingLocationId)
        {
            return OperationError.Invalid<StorageLocation>("Destination must differ from the receiving location.");
        }

        var isValid = await dbContext.StorageLocations
            .AnyAsync(x => x.Id == destinationStorageLocationId
                && x.WarehouseId == order.WarehouseId
                && !x.IsFolder
                && !x.DeletionMark
                && !x.Zone!.DeletionMark
                && x.Zone.Type == ZoneType.Storage, ct);

        return isValid
            ? OperationResult.Success()
            : OperationError.Invalid<StorageLocation>(
                "Putaway destination must be an active storage location in the order warehouse.");
    }

    private static async Task<OperationResult> ValidateSourceBalanceAsync(
        ApplicationDbContext dbContext,
        ReceivingOrder order,
        InventoryMovement movement,
        IReadOnlyCollection<InventoryMovement> draftMovements,
        Guid? excludedMovementId,
        CancellationToken ct)
    {
        var sourceBalance = await dbContext.InventoryBalances
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.WarehouseId == order.WarehouseId
                && x.StorageLocationId == order.ReceivingLocationId
                && x.StockKeepingUnitId == movement.StockKeepingUnitId, ct);

        var skuQuantity = draftMovements
            .Where(x => x.Id != excludedMovementId
                && x.StockKeepingUnitId == movement.StockKeepingUnitId)
            .Sum(x => x.Quantity) + movement.Quantity;

        if (sourceBalance is null || skuQuantity > sourceBalance.Quantity)
        {
            return OperationError.Invalid<InventoryMovement>(
                "Putaway quantity exceeds the available receiving-location balance.");
        }

        return OperationResult.Success();
    }

    private static async Task<OperationResult> ValidateCompletionDestinationsAsync(
        ApplicationDbContext dbContext,
        ReceivingOrder order,
        IReadOnlyCollection<InventoryMovement> draftMovements,
        CancellationToken ct)
    {
        var destinationIds = draftMovements
            .Select(x => x.DestinationStorageLocationId!.Value)
            .Distinct()
            .ToArray();

        var validDestinationCount = await dbContext.StorageLocations
            .CountAsync(x => destinationIds.Contains(x.Id)
                && x.WarehouseId == order.WarehouseId
                && !x.IsFolder
                && !x.DeletionMark
                && !x.Zone!.DeletionMark
                && x.Zone.Type == ZoneType.Storage, ct);

        return validDestinationCount == destinationIds.Length
            ? OperationResult.Success()
            : OperationError.Invalid<StorageLocation>(
                "Every putaway destination must remain an active storage location in the order warehouse.");
    }
}

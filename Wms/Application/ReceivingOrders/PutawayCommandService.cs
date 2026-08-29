using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wms.Application.Inventory;
using Wms.Application.Inventory.Movements;
using Wms.Application.StorageLocations;
using Wms.Common;
using Wms.Data;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Application.ReceivingOrders;

public class PutawayCommandService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    InventoryPostingService inventoryPostingService,
    ILogger<PutawayCommandService> logger)
{
    public async Task<OperationResult> StartAsync(Guid orderId, string userId, CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        var result = await StageStartAsync(dbContext, orderId, userId, ct);
        if (!result.IsSuccess)
        {
            return result;
        }

        await dbContext.SaveChangesAsync(ct);
        return OperationResult.Success();
    }

    internal async Task<OperationResult> StageStartAsync(
        ApplicationDbContext dbContext,
        Guid orderId,
        string userId,
        CancellationToken ct)
    {
        using var scope = logger.BeginScope("Putaway Start {OrderId}", orderId);

        var order = await LoadEditableOrderAsync(dbContext, orderId, ct);
        if (order is null)
        {
            return OperationError.NotFound($"Приходный ордер '{orderId}' не найден.");
        }

        var startResult = order.StartPutaway(DateTimeOffset.UtcNow, userId);
        if (!startResult.IsSuccess)
        {
            return startResult;
        }

        return startResult;
    }

    public async Task<OperationResult> AddMovementAsync(
        Guid orderId,
        int lineNumber,
        Guid destinationStorageLocationId,
        double quantity,
        CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        var result = await StageAddMovementAsync(
            dbContext,
            orderId,
            lineNumber,
            destinationStorageLocationId,
            quantity,
            ct);
        if (!result.IsSuccess)
        {
            return result.Error!;
        }

        await dbContext.SaveChangesAsync(ct);
        return OperationResult.Success();
    }

    internal async Task<OperationResult<InventoryMovement>> StageAddMovementAsync(
        ApplicationDbContext dbContext,
        Guid orderId,
        int lineNumber,
        Guid destinationStorageLocationId,
        double quantity,
        CancellationToken ct)
    {
        var order = await LoadEditableOrderAsync(dbContext, orderId, ct);

        if (order is null)
        {
            return OperationError.NotFound($"Приходный ордер '{orderId}' не найден.");
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
            return destinationResult.Error!;
        }

        var balanceResult = await ValidateSourceBalanceAsync(
            dbContext, order, movement, draftMovements, null, ct);
        if (!balanceResult.IsSuccess)
        {
            return balanceResult.Error!;
        }

        dbContext.InventoryMovements.Add(movement);
        return movement;
    }

    public async Task<OperationResult> UpdateMovementAsync(
        Guid movementId,
        Guid destinationStorageLocationId,
        double quantity,
        CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        var result = await StageUpdateMovementAsync(
            dbContext,
            movementId,
            destinationStorageLocationId,
            quantity,
            ct);
        if (!result.IsSuccess)
        {
            return result;
        }

        await dbContext.SaveChangesAsync(ct);
        return OperationResult.Success();
    }

    internal async Task<OperationResult> StageUpdateMovementAsync(
        ApplicationDbContext dbContext,
        Guid movementId,
        Guid destinationStorageLocationId,
        double quantity,
        CancellationToken ct)
    {
        var movement = await dbContext.InventoryMovements
            .FirstOrDefaultAsync(x => x.Id == movementId, ct);

        if (movement is null)
        {
            return OperationError.NotFound($"Движение размещения '{movementId}' не найдено.");
        }

        var order = movement.RecorderId is Guid orderId
            ? await LoadEditableOrderAsync(dbContext, orderId, ct)
            : null;
        if (order is null)
        {
            return OperationError.NotFound(
                $"Приходный ордер '{movement.RecorderId}' для движения размещения '{movementId}' не найден.");
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

        return OperationResult.Success();
    }

    public async Task<OperationResult> DeleteMovementAsync(Guid movementId, CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        var result = await StageDeleteMovementAsync(dbContext, null, movementId, ct);
        if (!result.IsSuccess)
        {
            return result;
        }

        await dbContext.SaveChangesAsync(ct);
        return OperationResult.Success();
    }

    internal Task<OperationResult> StageDeleteMovementAsync(
        ApplicationDbContext dbContext,
        Guid orderId,
        Guid movementId,
        CancellationToken ct) =>
        StageDeleteMovementAsync(dbContext, (Guid?)orderId, movementId, ct);

    private static async Task<OperationResult> StageDeleteMovementAsync(
        ApplicationDbContext dbContext,
        Guid? expectedOrderId,
        Guid movementId,
        CancellationToken ct)
    {
        var movement = await dbContext.InventoryMovements
            .FirstOrDefaultAsync(x => x.Id == movementId, ct);

        if (movement is null)
        {
            return OperationError.NotFound($"Движение размещения '{movementId}' не найдено.");
        }

        if (expectedOrderId is Guid expectedId && movement.RecorderId != expectedId)
        {
            return OperationError.NotFound(
                $"Движение размещения '{movementId}' не найдено в приходном ордере '{expectedId}'.");
        }

        var order = movement.RecorderId is Guid recorderOrderId
            ? await dbContext.ReceivingOrders.FirstOrDefaultAsync(x => x.Id == recorderOrderId, ct)
            : null;

        if (order is null)
        {
            return OperationError.NotFound(
                $"Приходный ордер '{movement.RecorderId}' для движения размещения '{movementId}' не найден.");
        }

        var removalResult = order.ValidatePutawayMovementRemoval(movement);
        if (!removalResult.IsSuccess)
        {
            return removalResult;
        }

        dbContext.InventoryMovements.Remove(movement);
        return OperationResult.Success();
    }

    public async Task<OperationResult> CompleteAsync(Guid orderId, string userId, CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        var result = await StageCompleteAsync(dbContext, orderId, userId, ct);
        return result.IsSuccess
            ? await InventoryPersistence.SaveChangesAsync(dbContext, ct)
            : result;
    }

    internal async Task<OperationResult> StageCompleteAsync(
        ApplicationDbContext dbContext,
        Guid orderId,
        string userId,
        CancellationToken ct)
    {
        using var scope = logger.BeginScope("Putaway Complete {OrderId}", orderId);

        var order = await LoadEditableOrderAsync(dbContext, orderId, ct);
        if (order is null)
        {
            return OperationError.NotFound($"Приходный ордер '{orderId}' не найден.");
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

        var postingResult = await inventoryPostingService
            .PostInventoryMovementsAsync(draftMovements, dbContext, ct);
        if (!postingResult.IsSuccess)
        {
            return postingResult;
        }

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
            return OperationError.Invalid("Позиция назначения должна отличаться от позиции приёмки.");
        }

        var destination = await dbContext.StorageLocations
            .Include(x => x.Zone)
            .Include(x => x.ActiveLock)
            .SingleOrDefaultAsync(x => x.Id == destinationStorageLocationId, ct);

        if (destination is null
            || destination.WarehouseId != order.WarehouseId
            || destination.IsFolder
            || destination.DeletionMark
            || destination.Zone?.DeletionMark == true
            || destination.Zone?.Type != ZoneType.Storage)
        {
            return OperationError.Invalid(
                "Позиция размещения должна быть активной позицией хранения на складе ордера.");
        }

        var destinationResult = StorageLocationAvailability.ValidateUnlocked(destination);
        if (!destinationResult.IsSuccess)
        {
            return destinationResult;
        }

        var source = await dbContext.StorageLocations
            .Include(x => x.Zone)
            .Include(x => x.ActiveLock)
            .SingleAsync(x => x.Id == order.ReceivingLocationId, ct);
        return StorageLocationAvailability.ValidateUnlocked(source);
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
            return OperationError.Invalid(
                "Количество размещения превышает доступный остаток в позиции приёмки.");
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
            : OperationError.Invalid(
                "Каждая позиция размещения должна оставаться активной позицией хранения на складе ордера.");
    }
}

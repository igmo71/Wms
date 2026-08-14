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
    public async Task<ServiceResult> StartAsync(Guid orderId, string userId, CancellationToken ct = default)
    {
        using var scope = logger.BeginScope("Putaway Start {OrderId}", orderId);

        if (string.IsNullOrWhiteSpace(userId))
            return ServiceError.Invalid<ReceivingOrder>("Starting user must be specified.");

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var order = await dbContext.ReceivingOrders
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == orderId, ct);

        if (order is null)
            return ServiceError.NotFound<ReceivingOrder>();

        var validationResult = order.ValidateToStartPutaway();
        if (!validationResult.IsSuccess)
            return validationResult;

        order.StartPutaway(userId);
        await dbContext.SaveChangesAsync(ct);

        return ServiceResult.Success();
    }

    public async Task<ServiceResult> AddMovementAsync(
        Guid orderId,
        int lineNumber,
        Guid destinationStorageLocationId,
        double quantity,
        CancellationToken ct = default)
    {
        if (quantity <= 0)
            return ServiceError.Invalid<InventoryMovement>("Putaway quantity must be greater than zero.");

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        var order = await LoadEditableOrderAsync(dbContext, orderId, ct);

        if (order is null)
            return ServiceError.NotFound<ReceivingOrder>();

        var validationResult = ValidateEditableOrder(order);
        if (!validationResult.IsSuccess)
            return validationResult;

        var destinationResult = await ValidateDestinationAsync(
            dbContext, order, destinationStorageLocationId, ct);
        if (!destinationResult.IsSuccess)
            return destinationResult;

        var orderItem = order.Items.FirstOrDefault(x => x.LineNumber == lineNumber);
        if (orderItem is null)
            return ServiceError.NotFound<ReceivingOrderItem>();

        var draftMovements = await LoadDraftMovementsAsync(dbContext, order.Id, ct);
        var limitsResult = await ValidateQuantityLimitsAsync(
            dbContext, order, orderItem, quantity, draftMovements, null, ct);
        if (!limitsResult.IsSuccess)
            return limitsResult;

        var movement = new InventoryMovement
        {
            WarehouseId = order.WarehouseId,
            SourceStorageLocationId = order.ReceivingLocationId,
            DestinationStorageLocationId = destinationStorageLocationId,
            StockKeepingUnitId = orderItem.StockKeepingUnitId,
            Quantity = quantity,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            RecorderType = RecorderType.ReceivingOrder,
            RecorderId = order.Id,
            RecorderLineNumber = orderItem.LineNumber
        };

        dbContext.InventoryMovements.Add(movement);
        await dbContext.SaveChangesAsync(ct);

        return ServiceResult.Success();
    }

    public async Task<ServiceResult> UpdateMovementAsync(
        Guid movementId,
        Guid destinationStorageLocationId,
        double quantity,
        CancellationToken ct = default)
    {
        if (quantity <= 0)
            return ServiceError.Invalid<InventoryMovement>("Putaway quantity must be greater than zero.");

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var movement = await dbContext.InventoryMovements
            .FirstOrDefaultAsync(x => x.Id == movementId, ct);

        if (movement is null)
            return ServiceError.NotFound<InventoryMovement>();

        var movementValidation = ValidateDraftPutawayMovement(movement);
        if (!movementValidation.IsSuccess)
            return movementValidation;

        var order = await LoadEditableOrderAsync(dbContext, movement.RecorderId!.Value, ct);
        if (order is null)
            return ServiceError.NotFound<ReceivingOrder>();

        var orderValidation = ValidateEditableOrder(order);
        if (!orderValidation.IsSuccess)
            return orderValidation;

        var destinationResult = await ValidateDestinationAsync(
            dbContext, order, destinationStorageLocationId, ct);
        if (!destinationResult.IsSuccess)
            return destinationResult;

        var orderItem = order.Items.FirstOrDefault(x => x.LineNumber == movement.RecorderLineNumber);
        if (orderItem is null)
            return ServiceError.NotFound<ReceivingOrderItem>();

        var draftMovements = await LoadDraftMovementsAsync(dbContext, order.Id, ct);
        var limitsResult = await ValidateQuantityLimitsAsync(
            dbContext, order, orderItem, quantity, draftMovements, movement.Id, ct);
        if (!limitsResult.IsSuccess)
            return limitsResult;

        movement.SourceStorageLocationId = order.ReceivingLocationId;
        movement.DestinationStorageLocationId = destinationStorageLocationId;
        movement.StockKeepingUnitId = orderItem.StockKeepingUnitId;
        movement.Quantity = quantity;
        movement.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(ct);
        return ServiceResult.Success();
    }

    public async Task<ServiceResult> DeleteMovementAsync(Guid movementId, CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var movement = await dbContext.InventoryMovements
            .FirstOrDefaultAsync(x => x.Id == movementId, ct);

        if (movement is null)
            return ServiceError.NotFound<InventoryMovement>();

        var movementValidation = ValidateDraftPutawayMovement(movement);
        if (!movementValidation.IsSuccess)
            return movementValidation;

        var order = await dbContext.ReceivingOrders
            .FirstOrDefaultAsync(x => x.Id == movement.RecorderId, ct);

        if (order is null)
            return ServiceError.NotFound<ReceivingOrder>();

        var orderValidation = ValidateEditableOrder(order);
        if (!orderValidation.IsSuccess)
            return orderValidation;

        dbContext.InventoryMovements.Remove(movement);
        await dbContext.SaveChangesAsync(ct);

        return ServiceResult.Success();
    }

    public async Task<ServiceResult> CompleteAsync(Guid orderId, string userId, CancellationToken ct = default)
    {
        using var scope = logger.BeginScope("Putaway Complete {OrderId}", orderId);

        if (string.IsNullOrWhiteSpace(userId))
            return ServiceError.Invalid<ReceivingOrder>("Completing user must be specified.");

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var order = await LoadEditableOrderAsync(dbContext, orderId, ct);
        if (order is null)
            return ServiceError.NotFound<ReceivingOrder>();

        var orderValidation = ValidateEditableOrder(order);
        if (!orderValidation.IsSuccess)
            return orderValidation;

        var draftMovements = await LoadDraftMovementsAsync(dbContext, order.Id, ct);
        var completionValidation = ValidateCompletion(order, draftMovements);
        if (!completionValidation.IsSuccess)
            return completionValidation;

        var destinationsValidation = await ValidateCompletionDestinationsAsync(
            dbContext, order, draftMovements, ct);
        if (!destinationsValidation.IsSuccess)
            return destinationsValidation;

        foreach (var movement in draftMovements)
            movement.ConfirmedBy = userId;

        var postingResult = await balanceAndTurnoverService
            .PostInventoryMovementsAsync(draftMovements, dbContext, ct);
        if (!postingResult.IsSuccess)
            return postingResult;

        order.CompletePutaway(userId);
        await dbContext.SaveChangesAsync(ct);

        return ServiceResult.Success();
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

    private static ServiceResult ValidateEditableOrder(ReceivingOrder order)
    {
        if (order.Status != ReceivingOrderStatus.Received
            || order.PutawayStatus != PutawayStatus.InProgress)
        {
            return ServiceError.Invalid<ReceivingOrder>(
                "Putaway movements can be changed only while putaway is in progress.");
        }

        if (order.ReceivingLocationId is null)
            return ServiceError.Invalid<ReceivingOrder>("Receiving location must be specified for putaway.");

        return ServiceResult.Success();
    }

    private static ServiceResult ValidateDraftPutawayMovement(InventoryMovement movement)
    {
        if (movement.PostedAtUtc is not null)
            return ServiceError.Invalid<InventoryMovement>("Posted putaway movement cannot be changed.");

        if (movement.RecorderType != RecorderType.ReceivingOrder
            || movement.RecorderId is null
            || movement.RecorderLineNumber is null
            || movement.SourceStorageLocationId is null
            || movement.DestinationStorageLocationId is null)
        {
            return ServiceError.Invalid<InventoryMovement>(
                "Movement does not belong to a receiving-order putaway line.");
        }

        return ServiceResult.Success();
    }

    private static async Task<ServiceResult> ValidateDestinationAsync(
        ApplicationDbContext dbContext,
        ReceivingOrder order,
        Guid destinationStorageLocationId,
        CancellationToken ct)
    {
        if (destinationStorageLocationId == order.ReceivingLocationId)
            return ServiceError.Invalid<StorageLocation>("Destination must differ from the receiving location.");

        var isValid = await dbContext.StorageLocations
            .AnyAsync(x => x.Id == destinationStorageLocationId
                && x.WarehouseId == order.WarehouseId
                && !x.DeletionMark
                && !x.Zone!.DeletionMark
                && x.Zone.Type == ZoneType.Storage, ct);

        return isValid
            ? ServiceResult.Success()
            : ServiceError.Invalid<StorageLocation>(
                "Putaway destination must be an active storage location in the order warehouse.");
    }

    private static async Task<ServiceResult> ValidateQuantityLimitsAsync(
        ApplicationDbContext dbContext,
        ReceivingOrder order,
        ReceivingOrderItem orderItem,
        double quantity,
        IReadOnlyCollection<InventoryMovement> draftMovements,
        Guid? excludedMovementId,
        CancellationToken ct)
    {
        var lineQuantity = draftMovements
            .Where(x => x.Id != excludedMovementId
                && x.RecorderLineNumber == orderItem.LineNumber)
            .Sum(x => x.Quantity) + quantity;

        if (lineQuantity > orderItem.FactQuantity)
            return ServiceError.Invalid<InventoryMovement>(
                "Putaway quantity exceeds the received quantity for the order line.");

        var sourceBalance = await dbContext.InventoryBalances
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.WarehouseId == order.WarehouseId
                && x.StorageLocationId == order.ReceivingLocationId
                && x.StockKeepingUnitId == orderItem.StockKeepingUnitId, ct);

        var skuQuantity = draftMovements
            .Where(x => x.Id != excludedMovementId
                && x.StockKeepingUnitId == orderItem.StockKeepingUnitId)
            .Sum(x => x.Quantity) + quantity;

        if (sourceBalance is null || skuQuantity > sourceBalance.Quantity)
            return ServiceError.Invalid<InventoryMovement>(
                "Putaway quantity exceeds the available receiving-location balance.");

        return ServiceResult.Success();
    }

    private static ServiceResult ValidateCompletion(
        ReceivingOrder order,
        IReadOnlyCollection<InventoryMovement> draftMovements)
    {
        if (draftMovements.Count == 0)
            return ServiceError.Invalid<ReceivingOrder>("Putaway has no movements.");

        if (draftMovements.Any(x => x.WarehouseId != order.WarehouseId
            || x.SourceStorageLocationId != order.ReceivingLocationId
            || x.DestinationStorageLocationId is null))
        {
            return ServiceError.Invalid<InventoryMovement>("Putaway contains an invalid movement.");
        }

        foreach (var item in order.Items)
        {
            var movements = draftMovements.Where(x => x.RecorderLineNumber == item.LineNumber).ToList();

            if (movements.Any(x => x.StockKeepingUnitId != item.StockKeepingUnitId)
                || movements.Sum(x => x.Quantity) != item.FactQuantity)
            {
                return ServiceError.Invalid<ReceivingOrder>(
                    "Every received order line must be fully allocated before completing putaway.");
            }
        }

        if (draftMovements.Any(x => order.Items.All(item => item.LineNumber != x.RecorderLineNumber)))
            return ServiceError.Invalid<InventoryMovement>("Putaway contains a movement for an unknown order line.");

        return ServiceResult.Success();
    }

    private static async Task<ServiceResult> ValidateCompletionDestinationsAsync(
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
                && !x.DeletionMark
                && !x.Zone!.DeletionMark
                && x.Zone.Type == ZoneType.Storage, ct);

        return validDestinationCount == destinationIds.Length
            ? ServiceResult.Success()
            : ServiceError.Invalid<StorageLocation>(
                "Every putaway destination must remain an active storage location in the order warehouse.");
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wms.Common;
using Wms.Data;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Application.ShippingOrders;

public class PickingCommandService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    ILogger<PickingCommandService> logger)
{
    public async Task<OperationResult> AddPickingMovementAsync(
        Guid orderId,
        int lineNumber,
        Guid sourceStorageLocationId,
        double quantity,
        CancellationToken ct = default)
    {
        using IDisposable? scope = logger.BeginScope("Picking AddMovement {OrderId} {LineNumber}", orderId, lineNumber);

        await using ApplicationDbContext dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        ShippingOrder? order = await LoadOrderAsync(dbContext, orderId, ct);
        if (order is null)
        {
            return OperationError.NotFound<ShippingOrder>();
        }

        List<InventoryMovement> draftMovements = await LoadDraftMovementsAsync(dbContext, order.Id, ct);
        OperationResult<InventoryMovement> movementResult = order.CreatePickingMovement(
            Guid.NewGuid(),
            lineNumber,
            sourceStorageLocationId,
            quantity,
            DateTimeOffset.UtcNow,
            draftMovements);
        if (!movementResult.IsSuccess)
        {
            return movementResult.Error!;
        }

        InventoryMovement movement = movementResult.Value!;
        OperationResult sourceResult = await ValidateSourceLocationAsync(
            dbContext, order, sourceStorageLocationId, ct);
        if (!sourceResult.IsSuccess)
        {
            return sourceResult;
        }

        OperationResult balanceResult = await ValidateSourceBalanceAsync(
            dbContext, order, movement, draftMovements, null, ct);
        if (!balanceResult.IsSuccess)
        {
            return balanceResult;
        }

        dbContext.InventoryMovements.Add(movement);
        await dbContext.SaveChangesAsync(ct);
        return OperationResult.Success();
    }

    public async Task<OperationResult> UpdatePickingMovementAsync(
        Guid movementId,
        Guid sourceStorageLocationId,
        double quantity,
        CancellationToken ct = default)
    {
        using IDisposable? scope = logger.BeginScope("Picking UpdateMovement {MovementId}", movementId);

        await using ApplicationDbContext dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        InventoryMovement? movement = await dbContext.InventoryMovements
            .FirstOrDefaultAsync(x => x.Id == movementId, ct);
        if (movement is null)
        {
            return OperationError.NotFound<InventoryMovement>();
        }

        ShippingOrder? order = movement.RecorderId is Guid orderId
            ? await LoadOrderAsync(dbContext, orderId, ct)
            : null;
        if (order is null)
        {
            return OperationError.NotFound<ShippingOrder>();
        }

        List<InventoryMovement> draftMovements = await LoadDraftMovementsAsync(dbContext, order.Id, ct);
        OperationResult updateResult = order.UpdatePickingMovement(
            movement,
            sourceStorageLocationId,
            quantity,
            DateTimeOffset.UtcNow,
            draftMovements);
        if (!updateResult.IsSuccess)
        {
            return updateResult;
        }

        OperationResult sourceResult = await ValidateSourceLocationAsync(
            dbContext, order, sourceStorageLocationId, ct);
        if (!sourceResult.IsSuccess)
        {
            return sourceResult;
        }

        OperationResult balanceResult = await ValidateSourceBalanceAsync(
            dbContext, order, movement, draftMovements, movement.Id, ct);
        if (!balanceResult.IsSuccess)
        {
            return balanceResult;
        }

        await dbContext.SaveChangesAsync(ct);
        return OperationResult.Success();
    }

    public async Task<OperationResult> DeletePickingMovementAsync(
        Guid movementId,
        CancellationToken ct = default)
    {
        using IDisposable? scope = logger.BeginScope("Picking DeleteMovement {MovementId}", movementId);

        await using ApplicationDbContext dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        InventoryMovement? movement = await dbContext.InventoryMovements
            .FirstOrDefaultAsync(x => x.Id == movementId, ct);
        if (movement is null)
        {
            return OperationError.NotFound<InventoryMovement>();
        }

        ShippingOrder? order = movement.RecorderId is Guid orderId
            ? await LoadOrderAsync(dbContext, orderId, ct)
            : null;
        if (order is null)
        {
            return OperationError.NotFound<ShippingOrder>();
        }

        List<InventoryMovement> draftMovements = await LoadDraftMovementsAsync(dbContext, order.Id, ct);
        OperationResult removalResult = order.RemovePickingMovement(movement, draftMovements);
        if (!removalResult.IsSuccess)
        {
            return removalResult;
        }

        dbContext.InventoryMovements.Remove(movement);
        await dbContext.SaveChangesAsync(ct);
        return OperationResult.Success();
    }

    private static Task<ShippingOrder?> LoadOrderAsync(
        ApplicationDbContext dbContext,
        Guid orderId,
        CancellationToken ct) =>
        dbContext.ShippingOrders
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == orderId, ct);

    private static Task<List<InventoryMovement>> LoadDraftMovementsAsync(
        ApplicationDbContext dbContext,
        Guid orderId,
        CancellationToken ct) =>
        dbContext.InventoryMovements
            .Where(x => x.PostedAtUtc == null
                && x.RecorderType == RecorderType.ShippingOrder
                && x.RecorderId == orderId)
            .ToListAsync(ct);

    private static async Task<OperationResult> ValidateSourceLocationAsync(
        ApplicationDbContext dbContext,
        ShippingOrder order,
        Guid sourceStorageLocationId,
        CancellationToken ct)
    {
        bool isValid = await dbContext.StorageLocations
            .AnyAsync(x => x.Id == sourceStorageLocationId
                && x.WarehouseId == order.WarehouseId
                && !x.IsFolder
                && !x.DeletionMark
                && !x.Zone!.DeletionMark
                && x.Zone.Type == ZoneType.Storage, ct);

        return isValid
            ? OperationResult.Success()
            : OperationError.Invalid<StorageLocation>(
                "Picking source must be an active storage location in the order warehouse.");
    }

    private static async Task<OperationResult> ValidateSourceBalanceAsync(
        ApplicationDbContext dbContext,
        ShippingOrder order,
        InventoryMovement movement,
        IReadOnlyCollection<InventoryMovement> draftMovements,
        Guid? excludedMovementId,
        CancellationToken ct)
    {
        InventoryBalance? sourceBalance = await dbContext.InventoryBalances
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.WarehouseId == order.WarehouseId
                && x.StorageLocationId == movement.SourceStorageLocationId
                && x.StockKeepingUnitId == movement.StockKeepingUnitId, ct);

        double sourceQuantity = draftMovements
            .Where(x => x.Id != excludedMovementId
                && x.SourceStorageLocationId == movement.SourceStorageLocationId
                && x.StockKeepingUnitId == movement.StockKeepingUnitId)
            .Sum(x => x.Quantity) + movement.Quantity;

        return sourceBalance is not null && sourceQuantity <= sourceBalance.Quantity
            ? OperationResult.Success()
            : OperationError.Invalid<InventoryMovement>(
                "Picking quantity exceeds the available inventory balance in the source storage location.");
    }
}

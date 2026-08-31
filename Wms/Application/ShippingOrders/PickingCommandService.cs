using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wms.Application.Persistence;
using Wms.Application.StorageLocations;
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
            return OperationError.NotFound($"Расходный ордер '{orderId}' не найден.");
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
        return await ApplicationPersistence.SaveChangesAsync(dbContext, ct);
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
            return OperationError.NotFound($"Движение отбора '{movementId}' не найдено.");
        }

        ShippingOrder? order = movement.RecorderId is Guid orderId
            ? await LoadOrderAsync(dbContext, orderId, ct)
            : null;
        if (order is null)
        {
            return OperationError.NotFound(
                $"Расходный ордер '{movement.RecorderId}' для движения отбора '{movementId}' не найден.");
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

        return await ApplicationPersistence.SaveChangesAsync(dbContext, ct);
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
            return OperationError.NotFound($"Движение отбора '{movementId}' не найдено.");
        }

        ShippingOrder? order = movement.RecorderId is Guid orderId
            ? await LoadOrderAsync(dbContext, orderId, ct)
            : null;
        if (order is null)
        {
            return OperationError.NotFound(
                $"Расходный ордер '{movement.RecorderId}' для движения отбора '{movementId}' не найден.");
        }

        List<InventoryMovement> draftMovements = await LoadDraftMovementsAsync(dbContext, order.Id, ct);
        OperationResult removalResult = order.RemovePickingMovement(movement, draftMovements);
        if (!removalResult.IsSuccess)
        {
            return removalResult;
        }

        dbContext.InventoryMovements.Remove(movement);
        return await ApplicationPersistence.SaveChangesAsync(dbContext, ct);
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
        var source = await dbContext.StorageLocations
            .Include(x => x.Zone)
            .Include(x => x.ActiveLock)
            .SingleOrDefaultAsync(x => x.Id == sourceStorageLocationId, ct);

        if (source is null
            || source.WarehouseId != order.WarehouseId
            || source.IsFolder
            || source.DeletionMark
            || source.Zone?.DeletionMark == true
            || source.Zone?.Type != ZoneType.Storage)
        {
            return OperationError.Invalid(
                "Источник отбора должен быть активной позицией хранения на складе ордера.");
        }

        var sourceResult = StorageLocationAvailability.ValidateUnlocked(source);
        if (!sourceResult.IsSuccess)
        {
            return sourceResult;
        }

        if (order.ShippingLocationId is not Guid shippingLocationId)
        {
            return OperationError.Invalid("Для отбора должна быть указана позиция отгрузки.");
        }

        var destination = await dbContext.StorageLocations
            .Include(x => x.Zone)
            .Include(x => x.ActiveLock)
            .SingleAsync(x => x.Id == shippingLocationId, ct);
        return StorageLocationAvailability.ValidateUnlocked(destination);
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
            : OperationError.Invalid(
                "Количество отбора превышает доступный остаток в позиции-источнике.");
    }
}

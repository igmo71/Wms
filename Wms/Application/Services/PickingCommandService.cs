using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wms.Common;
using Wms.Data;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Application.Services;

public class PickingCommandService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    ILogger<PickingCommandService> logger)
{
    public async Task<ServiceResult> AddPickingMovementAsync(
        Guid orderId,
        int lineNumber,
        Guid sourceStorageLocationId,
        double quantity,
        CancellationToken ct = default)
    {
        using var scope = logger.BeginScope("Picking AddMovement {OrderId} {LineNumber}", orderId, lineNumber);

        if (quantity <= 0)
            return ServiceError.Invalid<InventoryMovement>("Picking quantity must be greater than zero.");

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var order = await dbContext.ShippingOrders
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == orderId, ct);

        if (order is null)
            return ServiceError.NotFound<ShippingOrder>();

        var validationResult = ValidateEditablePickingOrder(order);

        if (!validationResult.IsSuccess)
            return validationResult;

        var sourceLocation = await dbContext.StorageLocations
            .FirstOrDefaultAsync(x => x.Id == sourceStorageLocationId, ct);

        if (sourceLocation is null)
            return ServiceError.NotFound<StorageLocation>();

        if (sourceLocation.WarehouseId != order.WarehouseId)
            return ServiceError.Invalid<StorageLocation>("Source storage location must belong to the shipping order warehouse.");

        if (sourceStorageLocationId == order.ShippingLocationId)
            return ServiceError.Invalid<StorageLocation>("Source storage location must differ from the shipping location.");

        var orderItem = order.Items.FirstOrDefault(x => x.LineNumber == lineNumber);

        if (orderItem is null)
            return ServiceError.NotFound<ShippingOrderItem>();

        var draftMovements = await dbContext.InventoryMovements
            .Where(x => x.PostedAtUtc == null
                && x.RecorderType == RecorderType.ShippingOrder
                && x.RecorderId == order.Id
                && x.RecorderLineNumber == lineNumber)
            .ToListAsync(ct);

        var now = DateTimeOffset.UtcNow;
        var movement = new InventoryMovement
        {
            WarehouseId = order.WarehouseId,
            SourceStorageLocationId = sourceStorageLocationId,
            DestinationStorageLocationId = order.ShippingLocationId,
            StockKeepingUnitId = orderItem.StockKeepingUnitId,
            Quantity = quantity,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RecorderType = RecorderType.ShippingOrder,
            RecorderId = order.Id,
            RecorderLineNumber = orderItem.LineNumber
        };

        dbContext.InventoryMovements.Add(movement);
        orderItem.FactQuantity = draftMovements.Sum(x => x.Quantity) + movement.Quantity;

        await dbContext.SaveChangesAsync(ct);
        return ServiceResult.Success();
    }

    public async Task<ServiceResult> UpdatePickingMovementAsync(
        Guid movementId,
        Guid sourceStorageLocationId,
        double quantity,
        CancellationToken ct = default)
    {
        using var scope = logger.BeginScope("Picking UpdateMovement {MovementId}", movementId);

        if (quantity <= 0)
            return ServiceError.Invalid<InventoryMovement>("Picking quantity must be greater than zero.");

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var movement = await dbContext.InventoryMovements
            .FirstOrDefaultAsync(x => x.Id == movementId, ct);

        if (movement is null)
            return ServiceError.NotFound<InventoryMovement>();

        if (movement.PostedAtUtc is not null)
            return ServiceError.Invalid<InventoryMovement>("Posted picking movement cannot be updated.");

        if (movement.RecorderType != RecorderType.ShippingOrder || movement.RecorderId is null || movement.RecorderLineNumber is null)
            return ServiceError.Invalid<InventoryMovement>("Movement does not belong to a shipping order line.");

        var order = await dbContext.ShippingOrders
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == movement.RecorderId, ct);

        if (order is null)
            return ServiceError.NotFound<ShippingOrder>();

        var validationResult = ValidateEditablePickingOrder(order);

        if (!validationResult.IsSuccess)
            return validationResult;

        var sourceLocation = await dbContext.StorageLocations
            .FirstOrDefaultAsync(x => x.Id == sourceStorageLocationId, ct);

        if (sourceLocation is null)
            return ServiceError.NotFound<StorageLocation>();

        if (sourceLocation.WarehouseId != order.WarehouseId)
            return ServiceError.Invalid<StorageLocation>("Source storage location must belong to the shipping order warehouse.");

        if (sourceStorageLocationId == order.ShippingLocationId)
            return ServiceError.Invalid<StorageLocation>("Source storage location must differ from the shipping location.");

        var orderItem = order.Items.FirstOrDefault(x => x.LineNumber == movement.RecorderLineNumber);

        if (orderItem is null)
            return ServiceError.NotFound<ShippingOrderItem>();

        var draftMovements = await dbContext.InventoryMovements
            .Where(x => x.PostedAtUtc == null
                && x.RecorderType == RecorderType.ShippingOrder
                && x.RecorderId == order.Id
                && x.RecorderLineNumber == movement.RecorderLineNumber)
            .ToListAsync(ct);

        movement.SourceStorageLocationId = sourceStorageLocationId;
        movement.Quantity = quantity;
        movement.UpdatedAtUtc = DateTimeOffset.UtcNow;

        orderItem.FactQuantity = draftMovements.Sum(x => x.Quantity);

        await dbContext.SaveChangesAsync(ct);
        return ServiceResult.Success();
    }

    public async Task<ServiceResult> DeletePickingMovementAsync(Guid movementId, CancellationToken ct = default)
    {
        using var scope = logger.BeginScope("Picking DeleteMovement {MovementId}", movementId);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var movement = await dbContext.InventoryMovements
            .FirstOrDefaultAsync(x => x.Id == movementId, ct);

        if (movement is null)
            return ServiceError.NotFound<InventoryMovement>();

        if (movement.PostedAtUtc is not null)
            return ServiceError.Invalid<InventoryMovement>("Posted picking movement cannot be deleted.");

        if (movement.RecorderType != RecorderType.ShippingOrder || movement.RecorderId is null || movement.RecorderLineNumber is null)
            return ServiceError.Invalid<InventoryMovement>("Movement does not belong to a shipping order line.");

        var order = await dbContext.ShippingOrders
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == movement.RecorderId, ct);

        if (order is null)
            return ServiceError.NotFound<ShippingOrder>();

        var validationResult = ValidateEditablePickingOrder(order);

        if (!validationResult.IsSuccess)
            return validationResult;

        var orderItem = order.Items.FirstOrDefault(x => x.LineNumber == movement.RecorderLineNumber);

        if (orderItem is null)
            return ServiceError.NotFound<ShippingOrderItem>();

        var draftMovements = await dbContext.InventoryMovements
            .Where(x => x.PostedAtUtc == null
                && x.RecorderType == RecorderType.ShippingOrder
                && x.RecorderId == order.Id
                && x.RecorderLineNumber == movement.RecorderLineNumber)
            .ToListAsync(ct);

        dbContext.InventoryMovements.Remove(movement);
        orderItem.FactQuantity = draftMovements
            .Where(x => x.Id != movement.Id)
            .Sum(x => x.Quantity);

        await dbContext.SaveChangesAsync(ct);
        return ServiceResult.Success();
    }

    private static ServiceResult ValidateEditablePickingOrder(ShippingOrder order)
    {
        var isEditable = order.Status is ShippingOrderStatus.ReadyForPicking
            or ShippingOrderStatus.ReadyForVerification
            or ShippingOrderStatus.InVerification
            or ShippingOrderStatus.Verified;

        if (!isEditable)
            return ServiceError.Invalid<ShippingOrder>("Picking movements can be changed only while the shipping order is being picked or verified.");

        if (order.ShippingLocationId is null)
            return ServiceError.Invalid<ShippingOrder>("Shipping location must be specified before changing picking movements.");

        return ServiceResult.Success();
    }
}

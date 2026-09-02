using System.Globalization;
using Wms.Application.MobileCommands;
using Wms.Common;
using Wms.Domain;

namespace Wms.Application.ShippingOrders;

public sealed class MobileShippingOrderCommandService(
    MobileCommandExecutor mobileCommandExecutor,
    ShippingOrderCommandService shippingOrderCommandService,
    PickingCommandService pickingCommandService)
{
    private const string StartPickingCommand = "shipping-order.start-picking";
    private const string AddPickingMovementCommand = "shipping-order.add-picking-movement";
    private const string DeletePickingMovementCommand = "shipping-order.delete-picking-movement";
    private const string CompletePickingCommand = "shipping-order.complete-picking";
    private const string ShipCommand = "shipping-order.ship";

    public Task<OperationResult<Guid>> StartPickingAsync(
        Guid orderId,
        string? shippingLocationBarcode,
        Guid clientRequestId,
        string userId,
        CancellationToken ct = default)
    {
        if (!StorageLocation.TryParseBarcode(shippingLocationBarcode, out var shippingLocationId))
        {
            return Task.FromResult<OperationResult<Guid>>(
                OperationError.Invalid("Некорректный QR-код ячейки."));
        }

        return mobileCommandExecutor.ExecuteAsync(
            StartPickingCommand,
            clientRequestId,
            Hash(orderId, shippingLocationId),
            userId,
            async (dbContext, token) =>
            {
                var result = await shippingOrderCommandService.StageStartPickingAsync(
                    dbContext,
                    orderId,
                    shippingLocationId,
                    userId,
                    token);
                return result.IsSuccess ? orderId : result.Error!;
            },
            ct);
    }

    public Task<OperationResult<Guid>> AddPickingMovementAsync(
        Guid orderId,
        int lineNumber,
        string? sourceStorageLocationBarcode,
        decimal quantity,
        Guid clientRequestId,
        string userId,
        CancellationToken ct = default)
    {
        if (!StorageLocation.TryParseBarcode(sourceStorageLocationBarcode, out var sourceStorageLocationId))
        {
            return Task.FromResult<OperationResult<Guid>>(
                OperationError.Invalid("Некорректный QR-код ячейки."));
        }

        return mobileCommandExecutor.ExecuteAsync(
            AddPickingMovementCommand,
            clientRequestId,
            Hash(orderId, lineNumber, sourceStorageLocationId, quantity),
            userId,
            async (dbContext, token) =>
            {
                var result = await pickingCommandService.StageAddPickingMovementAsync(
                    dbContext,
                    orderId,
                    lineNumber,
                    sourceStorageLocationId,
                    quantity,
                    token);
                return result.IsSuccess ? result.Value!.Id : result.Error!;
            },
            ct);
    }

    public Task<OperationResult<Guid>> DeletePickingMovementAsync(
        Guid orderId,
        Guid movementId,
        Guid clientRequestId,
        string userId,
        CancellationToken ct = default) =>
        mobileCommandExecutor.ExecuteAsync(
            DeletePickingMovementCommand,
            clientRequestId,
            Hash(orderId, movementId),
            userId,
            async (dbContext, token) =>
            {
                var result = await pickingCommandService.StageDeletePickingMovementAsync(
                    dbContext,
                    orderId,
                    movementId,
                    token);
                return result.IsSuccess ? movementId : result.Error!;
            },
            ct);

    public Task<OperationResult<Guid>> CompletePickingAsync(
        Guid orderId,
        Guid clientRequestId,
        string userId,
        CancellationToken ct = default) =>
        mobileCommandExecutor.ExecuteAsync(
            CompletePickingCommand,
            clientRequestId,
            Hash(orderId),
            userId,
            async (dbContext, token) =>
            {
                var result = await shippingOrderCommandService.StageSetReadyForShipmentAsync(
                    dbContext,
                    orderId,
                    userId,
                    token);
                return result.IsSuccess ? orderId : result.Error!;
            },
            ct);

    public Task<OperationResult<Guid>> ShipAsync(
        Guid orderId,
        Guid clientRequestId,
        string userId,
        CancellationToken ct = default)
    {
        return mobileCommandExecutor.ExecuteAsync(
            ShipCommand,
            clientRequestId,
            Hash(orderId),
            userId,
            async (dbContext, token) =>
            {
                var result = await shippingOrderCommandService.StageSetShippedAsync(
                    dbContext,
                    orderId,
                    userId,
                    token);
                return result.IsSuccess ? orderId : result.Error!;
            },
            ct);
    }

    private static string Hash(Guid orderId) =>
        MobileCommandExecutor.ComputeHash(orderId.ToString("N"));

    private static string Hash(Guid orderId, Guid shippingLocationId) =>
        MobileCommandExecutor.ComputeHash(string.Join(
            '|',
            orderId.ToString("N"),
            shippingLocationId.ToString("N")));

    private static string Hash(
        Guid orderId,
        int lineNumber,
        Guid sourceStorageLocationId,
        decimal quantity) =>
        MobileCommandExecutor.ComputeHash(string.Join(
            '|',
            orderId.ToString("N"),
            lineNumber.ToString(CultureInfo.InvariantCulture),
            sourceStorageLocationId.ToString("N"),
            quantity.ToString("G29", CultureInfo.InvariantCulture)));
}

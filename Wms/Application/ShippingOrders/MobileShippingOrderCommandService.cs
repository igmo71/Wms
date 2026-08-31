using Wms.Application.MobileCommands;
using Wms.Common;
using Wms.Domain;

namespace Wms.Application.ShippingOrders;

public sealed class MobileShippingOrderCommandService(
    MobileCommandExecutor mobileCommandExecutor,
    ShippingOrderCommandService shippingOrderCommandService)
{
    private const string StartPickingCommand = "shipping-order.start-picking";

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

    private static string Hash(Guid orderId, Guid shippingLocationId) =>
        MobileCommandExecutor.ComputeHash(string.Join(
            '|',
            orderId.ToString("N"),
            shippingLocationId.ToString("N")));
}

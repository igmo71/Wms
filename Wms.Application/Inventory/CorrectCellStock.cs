using System.Text.Json;
using Wms.Application.Authorization;
using Wms.Application.CommandExecution;
using Wms.Common;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Application.Inventory;

public sealed record CorrectCellStockRequest(Guid RequestId, string UserId,
    Guid WarehouseId, Guid LocationId, Guid SkuId, DateOnly? ExpirationDate, decimal QuantityChange);

public interface ICellStockCorrectionStore
{
    Task<bool> CanCorrectSkuAsync(ICommandExecutionContext session, Guid skuId, bool adding, CancellationToken ct);
}

public sealed class CorrectCellStock(CommandExecutor executor, ICellStockCorrectionStore store,
    PostInventoryMovement.Handler posting, WarehousePermission permission, TimeProvider clock)
{
    public const string PostedEventType = "inventory.cell-stock-correction.posted";

    public Task<OperationResult<Guid>> ExecuteAsync(CorrectCellStockRequest request, CancellationToken ct = default)
    {
        if (request.WarehouseId == Guid.Empty || request.LocationId == Guid.Empty || request.SkuId == Guid.Empty)
            return Task.FromResult<OperationResult<Guid>>(OperationError.Invalid("Выберите склад, ячейку и SKU."));

        var quantity = Math.Abs(request.QuantityChange);
        if (!WarehouseQuantity.IsPositive(quantity))
            return Task.FromResult<OperationResult<Guid>>(OperationError.Invalid("Укажите количество с точностью до трёх знаков."));

        var adding = request.QuantityChange > 0;
        var hash = CommandExecutor.ComputeHash(FormattableString.Invariant(
            $"{request.WarehouseId:N}:{request.LocationId:N}:{request.SkuId:N}:{request.ExpirationDate:yyyy-MM-dd}:{request.QuantityChange:G29}"));
        return executor.ExecuteAsync(PostedEventType, request.RequestId, hash, request.UserId,
            async (session, token) =>
            {
                var access = await permission.RequireOperatorOrManagerAsync(request.UserId, token);
                if (!access.IsSuccess)
                    return access.Error!;
                if (!await store.CanCorrectSkuAsync(session, request.SkuId, adding, token))
                    return OperationError.NotFound("Товар недоступен для корректировки.");

                var now = clock.GetUtcNow();
                var movementId = Guid.NewGuid();
                var movement = InventoryMovement.Create(movementId, request.WarehouseId,
                    adding ? null : request.LocationId, adding ? request.LocationId : null,
                    request.SkuId, quantity, now, RecorderType.StockCorrection, movementId, 1,
                    request.UserId, request.ExpirationDate);
                if (!movement.IsSuccess)
                    return movement.Error!;
                var posted = await posting.HandleAsync(new(movement.Value!, InventoryPostingKind.StockCorrection, now),
                    session, token);
                if (!posted.IsSuccess)
                    return posted.Error!;

                var audit = WarehouseAuditEvent.Create(PostedEventType, now, request.UserId, null,
                    null, null, request.SkuId, null, null,
                    JsonSerializer.Serialize(new { request.WarehouseId, request.ExpirationDate, request.QuantityChange }),
                    [movementId], commandUserId: request.UserId, commandType: PostedEventType,
                    requestId: request.RequestId, storageLocationId: request.LocationId);
                if (!audit.IsSuccess)
                    return audit.Error!;
                return new CommandExecutionResult(movementId, [audit.Value!]);
            }, ct);
    }
}

using System.Globalization;
using System.Text.Json;
using Wms.Application.Authorization;
using Wms.Application.CommandExecution;
using Wms.Application.Inventory;
using Wms.Common;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Application.Receiving;

public sealed record CorrectReceivingLpnQuantityCommand(
    Guid RequestId, Guid OrderId, Guid LpnId, decimal Quantity, string UserId);

public sealed class CorrectReceivingLpnQuantity(
    CommandExecutor executor, IReceivingWorkStore store,
    PostInventoryMovement.Handler posting, WarehousePermission permission,
    TimeProvider clock)
{
    private const string CommandType = "receiving.lpn.correct-quantity";

    public Task<OperationResult<Guid>> ExecuteAsync(
        CorrectReceivingLpnQuantityCommand command, CancellationToken ct = default)
    {
        if (command is null || command.OrderId == Guid.Empty || command.LpnId == Guid.Empty
            || !WarehouseQuantity.IsNonNegative(command.Quantity))
            return Task.FromResult<OperationResult<Guid>>(
                OperationError.Invalid("Для поправки нужны ордер, LPN и неотрицательное количество."));

        var hash = CommandExecutor.ComputeHash(
            $"{command.OrderId:N}:{command.LpnId:N}:"
            + command.Quantity.ToString("G29", CultureInfo.InvariantCulture));
        return executor.ExecuteAsync(CommandType, command.RequestId, hash, command.UserId,
            async (session, operationCt) =>
            {
                var access = await permission.RequireManagerAsync(command.UserId, operationCt);
                if (!access.IsSuccess)
                    return access.Error!;

                var order = await store.LoadOrderAsync(session, command.OrderId, operationCt);
                if (order is null)
                    return OperationError.NotFound("Приходный ордер не найден.");
                var lpn = await store.LoadLpnForPutawayAsync(session, command.LpnId, operationCt);
                if (lpn is null || lpn.ReceivingOrderId != order.Id)
                    return OperationError.NotFound("LPN не найден в ордере.");

                var activeItems = lpn.Items.Where(x => !x.IsDeleted).Take(2).ToArray();
                if (activeItems.Length != 1)
                    return OperationError.Conflict("Приёмный LPN должен содержать один действующий SKU.");
                var item = activeItems[0];
                var orderItem = order.Items.SingleOrDefault(x =>
                    x.StockKeepingUnitId == item.StockKeepingUnitId);
                if (orderItem is null)
                    return OperationError.NotFound("SKU отсутствует в составе ордера.");

                var oldQuantity = item.Quantity;
                var oldFact = orderItem.FactQuantity;
                var now = clock.GetUtcNow();
                if (command.Quantity > oldQuantity)
                {
                    var sku = await store.LoadSkuAsync(session, item.StockKeepingUnitId, operationCt);
                    if (sku is null)
                        return OperationError.NotFound("SKU не найден.");
                    if (sku.TracksExpirationDate && item.ExpirationDate is null)
                        return OperationError.Conflict(
                            "Для этого SKU не указан срок годности. Увеличение приёмки запрещено.");
                    if (item.ExpirationDate is DateOnly date
                        && date < DateOnly.FromDateTime(now.UtcDateTime))
                        return OperationError.Conflict(
                            "Срок годности LPN истёк. Увеличение приёмки запрещено.");
                    store.GuardClosingInputs(session, orderItem, sku);
                }
                var corrected = order.CorrectClosedReceivingLpnQuantity(
                    lpn, command.Quantity, command.UserId, now);
                if (!corrected.IsSuccess)
                    return corrected.Error!;

                var increase = command.Quantity > oldQuantity;
                var movement = InventoryMovement.Create(Guid.NewGuid(), order.WarehouseId,
                    increase ? null : lpn.OriginalReceivingLocationId,
                    increase ? lpn.OriginalReceivingLocationId : null,
                    item.StockKeepingUnitId, Math.Abs(command.Quantity - oldQuantity), now,
                    RecorderType.ReceivingOrder, order.Id,
                    1, command.UserId,
                    item.ExpirationDate, lpn.Id);
                if (!movement.IsSuccess)
                    return movement.Error!;
                var posted = await posting.HandleAsync(new PostInventoryMovement.Command(
                    movement.Value!, InventoryPostingKind.Correction, now), session, operationCt);
                if (!posted.IsSuccess)
                    return posted.Error!;

                var audit = WarehouseAuditEvent.Create("receiving.lpn.quantity-corrected", now,
                    command.UserId, null, order.Id, lpn.Id, item.StockKeepingUnitId, null,
                    JsonSerializer.Serialize(new { Quantity = oldQuantity, Fact = oldFact }),
                    JsonSerializer.Serialize(new
                    {
                        command.Quantity, Fact = corrected.Value,
                        lpn.OriginalReceivingLocationId, item.ExpirationDate
                    }),
                    [movement.Value!.Id], commandUserId: command.UserId,
                    commandType: CommandType, requestId: command.RequestId);
                if (!audit.IsSuccess)
                    return audit.Error!;
                return new CommandExecutionResult(lpn.Id, [audit.Value!]);
            }, ct);
    }
}

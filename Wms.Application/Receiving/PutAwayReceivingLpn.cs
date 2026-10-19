using System.Globalization;
using System.Text.Json;
using Wms.Application.Authorization;
using Wms.Application.CommandExecution;
using Wms.Application.Inventory;
using Wms.Common;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Application.Receiving;

public sealed record PutAwayReceivingLpnCommand(
    Guid RequestId, Guid LpnId, Guid LpnItemId,
    Guid DestinationStorageLocationId, decimal Quantity, string UserId);

public sealed class PutAwayReceivingLpn(
    CommandExecutor executor, IReceivingWorkStore store,
    PostInventoryMovement.Handler posting, WarehousePermission permission,
    TimeProvider clock)
{
    private const string CommandType = "receiving.lpn.putaway";

    public Task<OperationResult<Guid>> ExecuteAsync(
        PutAwayReceivingLpnCommand command, CancellationToken ct = default)
    {
        if (command is null || command.LpnId == Guid.Empty || command.LpnItemId == Guid.Empty
            || command.DestinationStorageLocationId == Guid.Empty
            || !WarehouseQuantity.IsPositive(command.Quantity))
            return Task.FromResult<OperationResult<Guid>>(
                OperationError.Invalid("Для размещения нужны LPN, строка, ячейка и положительное количество."));

        var hash = CommandExecutor.ComputeHash(
            $"{command.LpnId:N}:{command.LpnItemId:N}:{command.DestinationStorageLocationId:N}:"
            + command.Quantity.ToString("G29", CultureInfo.InvariantCulture));
        return executor.ExecuteAsync(CommandType, command.RequestId, hash, command.UserId,
            async (session, operationCt) =>
            {
                var access = await permission.RequireReceivingWorkerAsync(
                    command.UserId, operationCt);
                if (!access.IsSuccess)
                    return access.Error!;

                var lpn = await store.LoadLpnForPutawayAsync(
                    session, command.LpnId, operationCt);
                if (lpn is null)
                    return OperationError.NotFound("LPN не найдена.");
                if (lpn.ReceivingOrderId is not Guid orderId)
                    return OperationError.Conflict("LPN не относится к приходному ордеру.");
                var order = await store.LoadOrderAsync(session, orderId, operationCt);
                if (order is null)
                    return OperationError.NotFound("Приходный ордер не найден.");
                if (order.Status is not (ReceivingOrderStatus.InReceiving
                    or ReceivingOrderStatus.Received))
                    return OperationError.Conflict("Ордер ещё не находится в приёмке.");

                var item = lpn.Items.SingleOrDefault(x =>
                    x.Id == command.LpnItemId && !x.IsDeleted);
                if (item is null)
                    return OperationError.NotFound("Строка состава LPN не найдена.");
                var orderItem = order.Items.SingleOrDefault(x =>
                    x.StockKeepingUnitId == item.StockKeepingUnitId);
                if (orderItem is null)
                    return OperationError.Conflict("SKU LPN отсутствует в ордере.");

                var remainingBefore = item.Quantity - lpn.Putaways
                    .Where(x => x.LicensePlateNumberItemId == item.Id)
                    .Sum(x => x.Quantity);
                var now = clock.GetUtcNow();
                var movementId = Guid.NewGuid();
                var putaway = lpn.CreateReceivingPutaway(
                    item.Id, command.DestinationStorageLocationId,
                    command.Quantity, movementId, command.UserId, now);
                if (!putaway.IsSuccess)
                    return putaway.Error!;

                var movement = InventoryMovement.Create(movementId, order.WarehouseId,
                    lpn.OriginalReceivingLocationId, command.DestinationStorageLocationId,
                    item.StockKeepingUnitId, command.Quantity, now,
                    RecorderType.ReceivingOrder, order.Id,
                    1, command.UserId,
                    item.ExpirationDate, lpn.Id);
                if (!movement.IsSuccess)
                    return movement.Error!;
                var posted = await posting.HandleAsync(new PostInventoryMovement.Command(
                    movement.Value!, InventoryPostingKind.Putaway, now), session, operationCt);
                if (!posted.IsSuccess)
                    return posted.Error!;

                session.Stage(putaway.Value!);
                var audit = WarehouseAuditEvent.Create("receiving.lpn.putaway", now,
                    command.UserId, null, order.Id, lpn.Id, item.StockKeepingUnitId,
                    null,
                    JsonSerializer.Serialize(new { RemainingQuantity = remainingBefore }),
                    JsonSerializer.Serialize(new
                    {
                        command.Quantity, RemainingQuantity = remainingBefore - command.Quantity,
                        SourceStorageLocationId = lpn.OriginalReceivingLocationId,
                        command.DestinationStorageLocationId, item.ExpirationDate,
                        PutawayId = putaway.Value!.Id
                    }),
                    [movement.Value!.Id], commandUserId: command.UserId,
                    commandType: CommandType, requestId: command.RequestId);
                if (!audit.IsSuccess)
                    return audit.Error!;
                return new CommandExecutionResult(putaway.Value!.Id, [audit.Value!]);
            }, ct);
    }
}

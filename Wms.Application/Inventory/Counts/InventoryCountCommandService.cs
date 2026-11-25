using System.Text.Json;
using Wms.Application.Authorization;
using Wms.Application.CommandExecution;
using Wms.Application.Commands;
using Wms.Common;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Application.Inventory.Counts;

public sealed class InventoryCountCommandService(CommandExecutor executor, IInventoryCountStore store,
    PostInventoryMovement.Handler posting, WarehousePermission permission, TimeProvider clock)
{
    public Task<OperationResult<Guid>> StartAsync(StartInventoryCountCommand command,
        CommandContext context, CancellationToken ct = default) =>
        ExecuteAsync("start", command, context, async (session, now, token) =>
        {
            var locationId = command.StorageLocationId;
            if (locationId == Guid.Empty)
            {
                var code = StorageLocationAddress.Normalize(command.Barcode);
                if (!code.IsSuccess)
                    return code.Error!;
                locationId = await store.FindLocationIdAsync(session, command.WarehouseId, code.Value!, token)
                    ?? Guid.Empty;
            }

            var location = await store.LockLocationAsync(session, locationId, token);
            if (location is null)
                return OperationError.NotFound("Ячейка не найдена на выбранном складе.");
            if (location.WarehouseId != command.WarehouseId || !IsCountLocation(location))
                return OperationError.Invalid("Выберите активную ячейку Комплектации или Хранения.");
            if (location.ActiveLock is { OwnerType: StorageLocationLockOwnerType.InventoryCount, OwnerId: Guid countId })
                return new CountEffect(countId, location.Id, new { InventoryCountId = countId, Resumed = true });
            var block = await GetStockBlockAsync(session, location, token, location.ActiveLock?.Reason);
            if (block is not null)
                return block;

            var created = InventoryCount.Create(Guid.NewGuid(), now.LocalDateTime.ToString("yyMMdd-HHmmss"),
                now.LocalDateTime.Date, location.WarehouseId, location.Id, now, context.UserId);
            if (!created.IsSuccess)
                return created.Error!;
            var count = created.Value!;
            var balances = await store.LoadBalancesAsync(session, location.WarehouseId, location.Id, token);
            foreach (var balance in balances)
            {
                var added = count.AddExpectedItem(Guid.NewGuid(), balance.SkuId, balance.ExpirationDate,
                    balance.Quantity, now, context.UserId);
                if (!added.IsSuccess)
                    return added.Error!;
            }

            var hold = StorageLocationLock.CreateForInventoryCount(location.Id, count.Id,
                $"Инвентаризация {count.Number}", now, context.UserId);
            if (!hold.IsSuccess)
                return hold.Error!;
            session.Stage(count);
            session.Stage(hold.Value!);
            location.AdvanceOperationalRevision();
            return new CountEffect(count.Id, location.Id, new { InventoryCountId = count.Id });
        }, ct);

    public Task<OperationResult<Guid>> IncrementSkuAsync(IncrementInventoryCountSkuCommand command,
        CommandContext context, CancellationToken ct = default) =>
        EditAsync("scan", command.InventoryCountId, command, context, async (session, count, now, token) =>
        {
            var sku = await store.ResolveSkuAsync(session, command.Barcode, token);
            if (!sku.IsSuccess)
                return sku.Error!;
            var product = sku.Value!;
            if (command.ConfirmedSkuId is Guid skuId && skuId != product.Id)
                return OperationError.Conflict("Товар по штрихкоду изменился. Отсканируйте его снова.");
            var confirmed = command.ConfirmedSkuId.HasValue;
            var error = ValidateGroup(count, product, command.ExpirationDate, confirmed, 1);
            if (error is not null)
                return error;
            var itemId = Guid.NewGuid();
            var changed = count.IncrementSku(itemId, product.Id, command.ExpirationDate, confirmed,
                now, context.UserId);
            return RecordQuantity(session, count, changed, itemId);
        }, ct);

    public Task<OperationResult<Guid>> SetSkuCountedQuantityAsync(SetInventoryCountSkuQuantityCommand command,
        CommandContext context, CancellationToken ct = default) =>
        EditAsync("set-group", command.InventoryCountId, command, context, async (session, count, now, token) =>
        {
            var sku = await store.LoadSkuAsync(session, command.StockKeepingUnitId, token);
            if (sku is null)
                return OperationError.NotFound("Товар не найден.");
            var error = ValidateGroup(count, sku, command.ExpirationDate, command.ExpirationConfirmed,
                command.CountedQuantity);
            if (error is not null)
                return error;
            var itemId = Guid.NewGuid();
            var changed = count.SetSkuCountedQuantity(itemId, sku.Id, command.ExpirationDate,
                command.CountedQuantity, command.ExpirationConfirmed, now, context.UserId);
            return RecordQuantity(session, count, changed, itemId);
        }, ct);

    public Task<OperationResult<Guid>> SetCountedQuantityAsync(SetInventoryCountQuantityCommand command,
        CommandContext context, CancellationToken ct = default) =>
        EditAsync("set-quantity", command.InventoryCountId, command, context, (_, count, now, _) =>
        {
            var item = count.Items.SingleOrDefault(x => x.Id == command.ItemId);
            if (item is null)
                return Task.FromResult<OperationResult<CountEffect>>(
                    OperationError.NotFound("Строка инвентаризации не найдена."));
            var sku = item.StockKeepingUnit!;
            if (command.CountedQuantity > 0 && !command.ExpirationConfirmed
                && RequiresExpiration(count, sku, item.ExpirationDate))
                return Task.FromResult<OperationResult<CountEffect>>(
                    new InventoryCountExpirationRequired(sku.Id, sku.Name ?? string.Empty));

            var result = count.SetCountedQuantity(item.Id, command.CountedQuantity,
                command.ExpirationConfirmed, now, context.UserId);
            return Task.FromResult<OperationResult<CountEffect>>(
                result.IsSuccess ? QuantityEffect(count, item) : result.Error!);
        }, ct);

    public Task<OperationResult<Guid>> RemoveUnexpectedItemAsync(RemoveInventoryCountItemCommand command,
        CommandContext context, CancellationToken ct = default) =>
        EditAsync("remove-item", command.InventoryCountId, command, context, (session, count, now, _) =>
        {
            var item = count.Items.SingleOrDefault(x => x.Id == command.ItemId);
            var result = count.RemoveUnexpectedItem(command.ItemId, now, context.UserId);
            if (!result.IsSuccess)
                return Task.FromResult<OperationResult<CountEffect>>(result.Error!);
            store.Remove(session, item!);
            return Task.FromResult<OperationResult<CountEffect>>(new CountEffect(item!.Id, count.StorageLocationId,
                new { InventoryCountId = count.Id, ItemId = item.Id }));
        }, ct);

    public Task<OperationResult<Guid>> PostAsync(Guid inventoryCountId, CommandContext context,
        CancellationToken ct = default) =>
        ExecuteAsync("post", inventoryCountId, context, async (session, now, token) =>
        {
            var draft = await store.LoadDraftAsync(session, inventoryCountId, token);
            if (draft is null)
                return DraftUnavailable();
            var (count, location) = draft.Value;
            if (!IsCountLocation(location))
                return OperationError.Conflict("Ячейка стала недоступна для проведения.");
            var block = await GetStockBlockAsync(session, location, token);
            if (block is not null)
                return block;

            var balances = await store.LoadBalancesAsync(session, count.WarehouseId, location.Id, token);
            var expected = count.Items.Where(x => x.IsExpected)
                .ToDictionary(x => (x.StockKeepingUnitId, x.ExpirationDate), x => x.ExpectedQuantity);
            if (balances.Count != expected.Count || balances.Any(x =>
                    !expected.TryGetValue((x.SkuId, x.ExpirationDate), out var quantity) || quantity != x.Quantity))
                return OperationError.Conflict("Остатки ячейки изменились после начала инвентаризации.");
            if (count.Items.Any(x => x.CountedQuantity > 0 && !x.ExpirationConfirmed
                && RequiresExpiration(count, x.StockKeepingUnit!, x.ExpirationDate)))
                return OperationError.Conflict("Подтвердите срок годности для всех соответствующих строк.");
            if (!location.AllowsMultipleSkus
                && count.Items.Where(x => x.CountedQuantity > 0).Select(x => x.StockKeepingUnitId)
                    .Distinct().Skip(1).Any())
                return OperationError.Conflict(
                    "Факт содержит несколько SKU. Попросите менеджера разрешить смешение или удалите черновик перед перемещением товара.");

            var posted = count.Post(now, context.UserId);
            if (!posted.IsSuccess)
                return posted.Error!;
            var movementIds = new List<Guid>();
            foreach (var item in count.Items)
            {
                var difference = item.DifferenceQuantity!.Value;
                var remaining = Math.Abs(difference);
                while (remaining > 0)
                {
                    // A difference from negative accounting stock to positive fact can exceed one movement's range.
                    var quantity = Math.Min(remaining, WarehouseQuantity.Maximum);
                    var movement = InventoryMovement.Create(Guid.NewGuid(), count.WarehouseId,
                        difference < 0 ? location.Id : null, difference > 0 ? location.Id : null,
                        item.StockKeepingUnitId, quantity, now, RecorderType.InventoryCount, count.Id,
                        item.LineNumber, context.UserId, item.ExpirationDate);
                    if (!movement.IsSuccess)
                        return movement.Error!;
                    var result = await posting.HandleAsync(new PostInventoryMovement.Command(
                        movement.Value!, InventoryPostingKind.InventoryCount, now), session, token);
                    if (!result.IsSuccess)
                        return result.Error!;
                    movementIds.Add(result.Value);
                    remaining -= quantity;
                }
            }
            Release(session, location);
            return new CountEffect(count.Id, location.Id, new { InventoryCountId = count.Id }, movementIds);
        }, ct);

    public Task<OperationResult<Guid>> DeleteDraftAsync(Guid inventoryCountId, CommandContext context,
        CancellationToken ct = default) =>
        ExecuteAsync("delete-draft", inventoryCountId, context, async (session, _, token) =>
        {
            var draft = await store.LoadDraftAsync(session, inventoryCountId, token);
            if (draft is null)
                return DraftUnavailable();
            var (count, location) = draft.Value;
            store.Remove(session, count);
            Release(session, location);
            return new CountEffect(count.Id, location.Id, new { InventoryCountId = count.Id });
        }, ct);

    public Task<OperationResult<Guid>> SetSkuMixingAsync(Guid locationId, bool allowed, CommandContext context,
        CancellationToken ct = default) =>
        ExecuteAsync("set-sku-mixing", new { locationId, allowed }, context, async (session, _, token) =>
        {
            var location = await store.LockLocationAsync(session, locationId, token);
            if (location is null)
                return OperationError.NotFound("Ячейка не найдена.");
            if (!IsCountLocation(location))
                return OperationError.Invalid("Выберите активную ячейку Комплектации или Хранения.");
            if (!allowed)
            {
                var balances = await store.LoadBalancesAsync(session, location.WarehouseId, location.Id, token);
                if (balances.Select(x => x.SkuId).Distinct().Skip(1).Any())
                    return OperationError.Conflict("Нельзя запретить смешение, пока в ячейке есть остатки разных SKU.");
            }
            var previous = location.AllowsMultipleSkus;
            location.SetSkuMixing(allowed);
            return new CountEffect(location.Id, location.Id, new { AllowsMultipleSkus = allowed },
                Before: new { AllowsMultipleSkus = previous });
        }, ct, manager: true);

    public static bool RequiresExpiration(InventoryCount count, StockKeepingUnit sku, DateOnly? expirationDate) =>
        sku.TracksExpirationDate || expirationDate.HasValue
        || count.Items.Any(x => x.StockKeepingUnitId == sku.Id && x.ExpirationDate.HasValue);

    private async Task<OperationError?> GetStockBlockAsync(ICommandExecutionContext session,
        StorageLocation location, CancellationToken ct, string? holdReason = null)
    {
        var commitments = await store.GetStockCommitmentsAsync(session, location.Id, ct);
        var reasons = new List<string>();
        if (holdReason is not null)
            reasons.Add(holdReason);
        if (commitments.HasRouteReservation)
            reasons.Add("товар закреплён за сборкой");
        if (commitments.HasConfirmedPickReservation)
            reasons.Add("есть резерв подтверждённого отбора");
        if (commitments.HasOpenShortage)
            reasons.Add("недостача ожидает решения руководителя");
        return reasons.Count == 0 ? null : OperationError.Conflict(
            $"Ячейка {location.DisplayAddress} заблокирована — {string.Join(", ", reasons)}.");
    }

    private static OperationError? ValidateGroup(InventoryCount count, StockKeepingUnit sku,
        DateOnly? expirationDate, bool confirmed, decimal quantity)
    {
        if (sku.DeletionMark && !count.Items.Any(x => x.StockKeepingUnitId == sku.Id))
            return OperationError.Invalid("Товар недоступен.");
        if (quantity > 0 && !confirmed && RequiresExpiration(count, sku, expirationDate))
            return new InventoryCountExpirationRequired(sku.Id, sku.Name ?? string.Empty);
        return null;
    }

    private Task<OperationResult<Guid>> EditAsync(string action, Guid countId, object payload,
        CommandContext context, Func<ICommandExecutionContext, InventoryCount, DateTimeOffset,
            CancellationToken, Task<OperationResult<CountEffect>>> edit, CancellationToken ct) =>
        ExecuteAsync(action, payload, context, async (session, now, token) =>
        {
            var draft = await store.LoadDraftAsync(session, countId, token);
            return draft is { } state ? await edit(session, state.Count, now, token) : DraftUnavailable();
        }, ct);

    private Task<OperationResult<Guid>> ExecuteAsync(string action, object payload, CommandContext context,
        Func<ICommandExecutionContext, DateTimeOffset, CancellationToken, Task<OperationResult<CountEffect>>> execute,
        CancellationToken ct, bool manager = false)
    {
        var commandType = $"inventory-count.{action}";
        var hash = CommandExecutor.ComputeHash(JsonSerializer.Serialize(payload));
        return executor.ExecuteAsync(commandType, context.RequestId, hash, context.UserId, async (session, token) =>
        {
            var access = manager
                ? await permission.RequireManagerAsync(context.UserId, token)
                : await permission.RequireOperatorAsync(context.UserId, token);
            if (!access.IsSuccess)
                return access.Error!;
            var now = clock.GetUtcNow();
            var result = await execute(session, now, token);
            if (!result.IsSuccess)
                return result.Error!;
            var effect = result.Value!;
            var audit = WarehouseAuditEvent.Create(commandType, now, context.UserId, null, null, null, null, null,
                effect.Before is null ? null : JsonSerializer.Serialize(effect.Before),
                JsonSerializer.Serialize(effect.After), effect.MovementIds, commandUserId: context.UserId,
                commandType: commandType, requestId: context.RequestId, storageLocationId: effect.LocationId);
            return audit.IsSuccess ? new CommandExecutionResult(effect.Id, [audit.Value!]) : audit.Error!;
        }, ct);
    }

    private static OperationResult<CountEffect> RecordQuantity(ICommandExecutionContext session, InventoryCount count,
        OperationResult<InventoryCountItem> result, Guid newItemId)
    {
        if (!result.IsSuccess)
            return result.Error!;
        var item = result.Value!;
        if (item.Id == newItemId)
            session.Stage(item);
        return QuantityEffect(count, item);
    }

    private static CountEffect QuantityEffect(InventoryCount count, InventoryCountItem item) =>
        new(item.Id, count.StorageLocationId, new
        {
            InventoryCountId = count.Id, ItemId = item.Id, item.StockKeepingUnitId,
            item.ExpirationDate, item.ExpirationConfirmed, item.CountedQuantity
        });

    private void Release(ICommandExecutionContext session, StorageLocation location)
    {
        store.Remove(session, location.ActiveLock!);
        location.AdvanceOperationalRevision();
    }

    private static OperationError DraftUnavailable() =>
        OperationError.Conflict("Черновик инвентаризации не найден, закрыт или потерял удержание ячейки.");

    private static bool IsCountLocation(StorageLocation location) =>
        !location.IsFolder && !location.DeletionMark && location.Warehouse is { DeletionMark: false }
        && location.Zone is { DeletionMark: false, Type: ZoneType.Storage or ZoneType.Reserve }
        && location.Zone.WarehouseId == location.WarehouseId;

    private sealed record CountEffect(Guid Id, Guid LocationId, object After,
        IReadOnlyCollection<Guid>? MovementIds = null, object? Before = null);
}

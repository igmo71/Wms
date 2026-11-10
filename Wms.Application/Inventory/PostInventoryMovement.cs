using Wms.Application.CommandExecution;
using Wms.Common;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Application.Inventory;

public enum InventoryPostingKind
{
    Receipt,
    Putaway,
    Correction,
    Reversal,
    PickingIssue,
    Replenishment,
    StockCorrection
}

public sealed record InventoryPostingState(
    IReadOnlyDictionary<Guid, StorageLocation> Locations,
    IReadOnlyDictionary<Guid, InventoryBalance> Balances)
{
    public IReadOnlyList<PickingReservation> Reservations { get; init; } = [];
    public IReadOnlyList<StockShortage> Shortages { get; init; } = [];
}

public sealed record InventoryPostingOwner(StorageLocationLockOwnerType Type, Guid Id);

public interface IInventoryPostingStore
{
    Task<InventoryPostingState> LoadAsync(
        ICommandExecutionContext context,
        InventoryMovement movement,
        CancellationToken ct);

    Task<bool> HasMultipleNonzeroSkusAsync(
        ICommandExecutionContext context, Guid warehouseId,
        Guid storageLocationId, CancellationToken ct);
}

public static class PostInventoryMovement
{
    public sealed record Command(
        InventoryMovement Movement,
        InventoryPostingKind Kind,
        DateTimeOffset PostedAtUtc,
        Guid? PickingEntryId = null,
        Guid? ShortageId = null,
        InventoryPostingOwner? CorrectionHoldOwner = null);

    public sealed class Handler(IInventoryPostingStore store)
    {
        public async Task<OperationResult<Guid>> HandleAsync(
            Command command, ICommandExecutionContext context, CancellationToken ct)
        {
            var movement = command.Movement;
            if (movement is null || !WarehouseQuantity.IsPositive(movement.Quantity))
                return OperationError.Invalid("Для проведения нужно положительное количество движения.");
            var newStockOperation = command.Kind is InventoryPostingKind.PickingIssue
                or InventoryPostingKind.Replenishment or InventoryPostingKind.StockCorrection;
            if (!newStockOperation && (movement.LicensePlateNumberId is null
                || movement.RecorderType != RecorderType.ReceivingOrder))
                return OperationError.Invalid("Для проведения приемки нужна ссылка на LPN.");
            if (newStockOperation && (movement.RecorderId is null || movement.RecorderId == Guid.Empty
                || movement.RecorderType != (command.Kind switch
                {
                    InventoryPostingKind.PickingIssue => RecorderType.PickingAssembly,
                    InventoryPostingKind.Replenishment => RecorderType.ReplenishmentTask,
                    _ => RecorderType.StockCorrection
                })))
                return OperationError.Invalid("Регистратор движения не соответствует складской операции.");

            if (command.Kind == InventoryPostingKind.Reversal && movement.ReversedMovementId is null
                || command.Kind is not (InventoryPostingKind.Reversal or InventoryPostingKind.StockCorrection)
                    && movement.ReversedMovementId is not null)
                return OperationError.Invalid("Обратное движение должно ссылаться на исходное.");

            var sourceId = movement.SourceStorageLocationId;
            var destinationId = movement.DestinationStorageLocationId;
            if (command.CorrectionHoldOwner is { } correctionOwner
                && (command.Kind != InventoryPostingKind.StockCorrection || correctionOwner.Id == Guid.Empty
                    || correctionOwner.Type is not (StorageLocationLockOwnerType.PickingAssembly
                        or StorageLocationLockOwnerType.ReplenishmentTask)))
                return OperationError.Invalid("Владелец удержания корректировки указан некорректно.");
            if (command.Kind switch
                {
                    InventoryPostingKind.Receipt => sourceId is not null || destinationId is null,
                    InventoryPostingKind.Putaway => sourceId is null || destinationId is null,
                    InventoryPostingKind.Correction => (sourceId is null) == (destinationId is null),
                    InventoryPostingKind.Reversal => false,
                    InventoryPostingKind.PickingIssue => sourceId is null || destinationId is not null,
                    InventoryPostingKind.Replenishment => sourceId is null || destinationId is null,
                    InventoryPostingKind.StockCorrection => false,
                    _ => true
                })
                return OperationError.Invalid("Направление движения не соответствует операции.");

            var state = await store.LoadAsync(context, movement, ct);
            var owner = command.Kind switch
            {
                InventoryPostingKind.PickingIssue => new InventoryPostingOwner(
                    StorageLocationLockOwnerType.PickingAssembly, movement.RecorderId!.Value),
                InventoryPostingKind.Replenishment => new InventoryPostingOwner(
                    StorageLocationLockOwnerType.ReplenishmentTask, movement.RecorderId!.Value),
                InventoryPostingKind.StockCorrection => command.CorrectionHoldOwner,
                _ => null
            };
            var locationIds = new[] { sourceId, destinationId }
                .OfType<Guid>().Distinct().ToArray();
            foreach (var locationId in locationIds)
            {
                if (!state.Locations.TryGetValue(locationId, out var location)
                    || location.WarehouseId != movement.WarehouseId)
                    return OperationError.Invalid("Складская позиция движения не найдена на выбранном складе.");

                var locationHold = location.ActiveLock;
                var owned = locationHold is not null && owner is not null
                    && locationHold.OwnerType == owner.Type && locationHold.OwnerId == owner.Id;
                if (command.Kind == InventoryPostingKind.Reversal)
                {
                    if (locationHold?.OwnerType is StorageLocationLockOwnerType.PickingAssembly
                        or StorageLocationLockOwnerType.ReplenishmentTask)
                        return OperationError.Conflict("Ячейка удерживается Отбором или Подпиткой.");
                    continue;
                }
                if (location.IsFolder || location.DeletionMark || locationHold is not null && !owned
                    || location.Warehouse is null || location.Warehouse.DeletionMark
                    || location.Zone is null || location.Zone.DeletionMark
                    || location.Zone.WarehouseId != movement.WarehouseId)
                    return OperationError.Conflict("Складская позиция недоступна для проведения.");

                location.AdvanceOperationalRevision();
            }

            if ((command.Kind is InventoryPostingKind.Receipt or InventoryPostingKind.Correction)
                && state.Locations[(destinationId ?? sourceId)!.Value].Zone!.Type != ZoneType.Receiving)
                return OperationError.Invalid("Приход и поправка количества проводятся в зоне приемки.");
            if (command.Kind == InventoryPostingKind.Putaway
                && (state.Locations[sourceId!.Value].Zone!.Type != ZoneType.Receiving
                    || state.Locations[destinationId!.Value].Zone!.Type is not
                        (ZoneType.Storage or ZoneType.Reserve)))
                return OperationError.Invalid(
                    "Размещение проводится из зоны приёмки в ячейку комплектации или хранения.");

            if (command.Kind == InventoryPostingKind.PickingIssue
                && (state.Locations[sourceId!.Value].Zone!.Type is not (ZoneType.Storage or ZoneType.Reserve)
                    || state.Locations[sourceId.Value].Zone!.Type == ZoneType.Reserve
                        && state.Locations[sourceId.Value].ActiveLock is null))
                return OperationError.Conflict("Отбор из Хранения требует удержания снятого поддона за сборкой.");
            if (command.Kind == InventoryPostingKind.Replenishment
                && (state.Locations[sourceId!.Value].Zone!.Type != ZoneType.Reserve
                    || state.Locations[sourceId.Value].ActiveLock is null
                    || state.Locations[destinationId!.Value].Zone!.Type != ZoneType.Storage
                    || state.Locations[destinationId.Value].ActiveLock is not null))
                return OperationError.Conflict("Подпитка проводится из удерживаемого Хранения в свободную Комплектацию.");
            if (command.Kind == InventoryPostingKind.StockCorrection
                && locationIds.Any(x => state.Locations[x].Zone!.Type is not (ZoneType.Storage or ZoneType.Reserve)))
                return OperationError.Invalid("Корректировка остатка выполняется в Комплектации или Хранении.");
            if (command.Kind is InventoryPostingKind.PickingIssue or InventoryPostingKind.Replenishment
                && movement.ExpirationDate is DateOnly expiry
                && expiry < DateOnly.FromDateTime(command.PostedAtUtc.UtcDateTime))
                return OperationError.Conflict("Срок годности выбранного товара истёк.");

            PickingReservation? ownReservation = null;
            if (command.Kind == InventoryPostingKind.PickingIssue)
            {
                ownReservation = state.Reservations.SingleOrDefault(x => x.PickingEntryId == command.PickingEntryId
                    && x.PickingAssemblyId == movement.RecorderId && x.StorageLocationId == sourceId
                    && x.WarehouseId == movement.WarehouseId && x.StockKeepingUnitId == movement.StockKeepingUnitId
                    && x.ExpirationDate == movement.ExpirationDate && x.CompletedAtUtc is null);
                if (ownReservation is null || ownReservation.Quantity != movement.Quantity)
                    return OperationError.Conflict("Списание должно совпадать с собственным резервом записи Отбора.");
            }
            StockShortage? resolvedShortage = null;
            if (command.ShortageId is Guid shortageId)
            {
                resolvedShortage = state.Shortages.SingleOrDefault(x => x.Id == shortageId);
                if (command.Kind != InventoryPostingKind.StockCorrection || resolvedShortage is null
                    || movement.RecorderId != shortageId)
                    return OperationError.Invalid("Корректировка должна ссылаться на свою активную недостачу.");
            }
            if (sourceId is Guid protectedSource)
            {
                var reserved = state.Reservations.Where(x => x.StorageLocationId == protectedSource
                    && x != ownReservation).Sum(x => x.Quantity);
                var blocked = state.Shortages.Where(x => x.StorageLocationId == protectedSource
                    && x != resolvedShortage).Sum(x => x.BlockedQuantity);
                var accounting = state.Balances.GetValueOrDefault(protectedSource)?.Quantity ?? 0;
                if ((newStockOperation || reserved + blocked > 0)
                    && accounting - movement.Quantity < reserved + blocked)
                    return OperationError.Conflict("Недостаточно остатка после защиты резервов и спорного количества.");
            }

            if (command.Kind == InventoryPostingKind.Putaway
                && (!state.Balances.TryGetValue(sourceId!.Value, out var available)
                    || available.Quantity < movement.Quantity))
                return OperationError.Conflict("Недостаточно доступного остатка для размещения.");

            var post = movement.Post(command.PostedAtUtc);
            if (!post.IsSuccess)
                return post.Error!;

            if (sourceId is Guid source)
            {
                var sourceResult = ApplySide(movement, source, -movement.Quantity,
                    command.Kind, command.PostedAtUtc, state, context);
                if (!sourceResult.IsSuccess)
                    return sourceResult.Error!;
            }
            if (destinationId is Guid destination)
            {
                var destinationResult = ApplySide(movement, destination, movement.Quantity,
                    command.Kind, command.PostedAtUtc, state, context);
                if (!destinationResult.IsSuccess)
                    return destinationResult.Error!;

                var location = state.Locations[destination];
                if ((location.Zone?.Type is ZoneType.Storage or ZoneType.Reserve)
                    && !location.AllowsMultipleSkus
                    && await store.HasMultipleNonzeroSkusAsync(
                        context, movement.WarehouseId, destination, ct))
                    return OperationError.Conflict(
                        $"В ячейке {location.DisplayAddress} запрещено хранить несколько SKU; пополнение недоступно, пока смешение не устранено.");
            }

            if (ownReservation is not null)
            {
                var completed = ownReservation.Complete(command.PostedAtUtc);
                if (!completed.IsSuccess)
                    return completed.Error!;
            }
            context.Stage(movement);
            return movement.Id;
        }

        private static OperationResult ApplySide(
            InventoryMovement movement,
            Guid locationId,
            decimal delta,
            InventoryPostingKind kind,
            DateTimeOffset postedAtUtc,
            InventoryPostingState state,
            ICommandExecutionContext context)
        {
            var allowNegative = kind is InventoryPostingKind.Correction or InventoryPostingKind.Reversal;
            InventoryBalanceChange change;
            if (state.Balances.TryGetValue(locationId, out var balance))
            {
                var adjusted = balance.Adjust(delta, postedAtUtc,
                    allowNegative || delta > 0 && balance.Quantity < 0);
                if (!adjusted.IsSuccess)
                    return adjusted.Error!;
                change = adjusted.Value;
            }
            else
            {
                if (delta < 0 && !allowNegative)
                    return OperationError.Conflict("Остаток в месте источника не найден.");
                var created = InventoryBalance.Create(Guid.NewGuid(), movement.WarehouseId,
                    locationId, movement.StockKeepingUnitId, delta, postedAtUtc,
                    movement.ExpirationDate, allowNegative);
                if (!created.IsSuccess)
                    return created.Error!;
                context.Stage(created.Value!);
                change = new InventoryBalanceChange(0, delta, delta);
            }

            var turnover = InventoryTurnover.Create(Guid.NewGuid(), movement.WarehouseId,
                locationId, movement.StockKeepingUnitId, change, postedAtUtc,
                movement.Id, movement.ExpirationDate);
            if (!turnover.IsSuccess)
                return turnover.Error!;
            context.Stage(turnover.Value!);
            return OperationResult.Success();
        }
    }
}

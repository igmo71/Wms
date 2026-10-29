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
    Reversal
}

public sealed record InventoryPostingState(
    IReadOnlyDictionary<Guid, StorageLocation> Locations,
    IReadOnlyDictionary<Guid, InventoryBalance> Balances);

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
        DateTimeOffset PostedAtUtc);

    public sealed class Handler(IInventoryPostingStore store)
    {
        public async Task<OperationResult<Guid>> HandleAsync(
            Command command, ICommandExecutionContext context, CancellationToken ct)
        {
            var movement = command.Movement;
            if (movement is null || movement.LicensePlateNumberId is null
                || movement.RecorderType != RecorderType.ReceivingOrder
                || !WarehouseQuantity.IsPositive(movement.Quantity))
                return OperationError.Invalid("Для проведения приемки нужна положительная величина и ссылка на LPN.");

            if ((command.Kind == InventoryPostingKind.Reversal)
                != (movement.ReversedMovementId is not null))
                return OperationError.Invalid("Обратное движение должно ссылаться на исходное.");

            var sourceId = movement.SourceStorageLocationId;
            var destinationId = movement.DestinationStorageLocationId;
            if (command.Kind switch
                {
                    InventoryPostingKind.Receipt => sourceId is not null || destinationId is null,
                    InventoryPostingKind.Putaway => sourceId is null || destinationId is null,
                    InventoryPostingKind.Correction => (sourceId is null) == (destinationId is null),
                    InventoryPostingKind.Reversal => false,
                    _ => true
                })
                return OperationError.Invalid("Направление движения не соответствует операции.");

            var state = await store.LoadAsync(context, movement, ct);
            var locationIds = new[] { sourceId, destinationId }
                .OfType<Guid>().Distinct().ToArray();
            foreach (var locationId in locationIds)
            {
                if (!state.Locations.TryGetValue(locationId, out var location)
                    || location.WarehouseId != movement.WarehouseId)
                    return OperationError.Invalid("Складская позиция движения не найдена на выбранном складе.");

                if (command.Kind == InventoryPostingKind.Reversal)
                    continue;
                if (location.IsFolder || location.DeletionMark || location.ActiveLock is not null
                    || location.Warehouse is null || location.Warehouse.DeletionMark
                    || location.Zone is null || location.Zone.DeletionMark)
                    return OperationError.Conflict("Складская позиция недоступна для проведения.");

                location.AdvanceOperationalRevision();
            }

            if ((command.Kind is InventoryPostingKind.Receipt or InventoryPostingKind.Correction)
                && state.Locations[(destinationId ?? sourceId)!.Value].Zone!.Type != ZoneType.Receiving)
                return OperationError.Invalid("Приход и поправка количества проводятся в зоне приемки.");
            if (command.Kind == InventoryPostingKind.Putaway
                && (state.Locations[sourceId!.Value].Zone!.Type != ZoneType.Receiving
                    || state.Locations[destinationId!.Value].Zone!.Type != ZoneType.Storage))
                return OperationError.Invalid("Размещение проводится из зоны приемки в зону хранения.");

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
                if (location.Zone?.Type == ZoneType.Storage
                    && !location.AllowsMultipleSkus
                    && await store.HasMultipleNonzeroSkusAsync(
                        context, movement.WarehouseId, destination, ct))
                    return OperationError.Conflict(
                        $"В ячейке {location.DisplayAddress} запрещено хранить несколько SKU.");
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

using System.Text.Json;
using Wms.Application.Authorization;
using Wms.Application.CommandExecution;
using Wms.Common;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Application.Receiving;

public sealed record ReceivingOrderDeletionLpnPreview(
    Guid Id, string Code, LicensePlateNumberStatus Status);

public sealed record ReceivingOrderDeletionPreview(
    Guid OrderId, string OrderNumber, string SnapshotToken,
    IReadOnlyList<ReceivingOrderDeletionLpnPreview> Lpns,
    IReadOnlyList<ReceivingLpnReversalPreview> Reversals,
    IReadOnlyList<ReceivingLpnBalancePreview> Balances,
    bool RequiresNegativeConfirmation);

public sealed record ReceivingOrderDeletionState(
    ReceivingOrder Order,
    IReadOnlyList<InventoryMovement> Movements,
    IReadOnlyList<InventoryBalance> Balances,
    IReadOnlyList<StorageLocation> Locations,
    IReadOnlySet<Guid> ZeroConfirmedSkuIds);

public interface IReceivingOrderDeletionStore
{
    Task<ReceivingOrderDeletionState?> LoadPreviewAsync(Guid orderId, CancellationToken ct);
    Task<ReceivingOrderDeletionState?> LoadForDeletionAsync(
        ICommandExecutionContext session, Guid orderId, CancellationToken ct);
}

public sealed class PreviewReceivingOrderDeletion(
    IReceivingOrderDeletionStore store, WarehousePermission permission)
{
    public async Task<OperationResult<ReceivingOrderDeletionPreview>> ExecuteAsync(
        Guid orderId, string userId, CancellationToken ct = default)
    {
        if (orderId == Guid.Empty)
            return OperationError.Invalid("Для предпросмотра нужен приходный ордер.");
        var access = await permission.RequireManagerAsync(userId, ct);
        if (!access.IsSuccess)
            return access.Error!;
        var state = await store.LoadPreviewAsync(orderId, ct);
        return state is null
            ? OperationError.NotFound("Приходный ордер не найден.")
            : ReceivingOrderDeletionPreviewFactory.Create(state);
    }
}

internal static class ReceivingOrderDeletionPreviewFactory
{
    private readonly record struct StockKey(
        Guid WarehouseId, Guid LocationId, Guid SkuId, DateOnly? ExpirationDate);

    public static OperationResult<ReceivingOrderDeletionPreview> Create(
        ReceivingOrderDeletionState state)
    {
        var order = state.Order;
        if (order.Status == ReceivingOrderStatus.Deleted)
            return OperationError.Conflict("Приходный ордер уже удалён.");

        var lpns = order.LicensePlateNumbers.OrderBy(x => x.Id).ToArray();
        if (lpns.Any(x => x.Putaways.Count != 0))
            return OperationError.Conflict(
                "После начала размещения любого LPN удалить приходный ордер нельзя.");
        if (lpns.Any(x => x.ReceivingOrderId != order.Id
            || x.Status == LicensePlateNumberStatus.Available))
            return OperationError.Conflict("Состав LPN приходного ордера некорректен.");

        var lpnById = lpns.ToDictionary(x => x.Id);
        var originals = state.Movements.Where(x => x.ReversedMovementId is null)
            .OrderByDescending(x => x.PostedAtUtc).ThenByDescending(x => x.Id).ToArray();
        var originalById = originals.ToDictionary(x => x.Id);
        var reversalByOriginal = new Dictionary<Guid, InventoryMovement>();
        foreach (var reversal in state.Movements.Where(x => x.ReversedMovementId is not null))
        {
            if (reversal.ReversedMovementId is not Guid originalId
                || !originalById.TryGetValue(originalId, out var original)
                || !reversalByOriginal.TryAdd(originalId, reversal)
                || reversal.WarehouseId != original.WarehouseId
                || reversal.LicensePlateNumberId != original.LicensePlateNumberId
                || reversal.RecorderType != RecorderType.ReceivingOrder
                || reversal.RecorderId != order.Id
                || reversal.StockKeepingUnitId != original.StockKeepingUnitId
                || reversal.ExpirationDate != original.ExpirationDate
                || reversal.Quantity != original.Quantity
                || reversal.SourceStorageLocationId != original.DestinationStorageLocationId
                || reversal.DestinationStorageLocationId != original.SourceStorageLocationId)
                return OperationError.Conflict("История обратных движений ордера неполна.");
        }

        foreach (var lpn in lpns)
        {
            var own = originals.Where(x => x.LicensePlateNumberId == lpn.Id).ToArray();
            if (lpn.ClosedAtUtc is null)
            {
                if (own.Length != 0 || lpn.Status == LicensePlateNumberStatus.Closed)
                    return OperationError.Conflict("История движений LPN не соответствует его состоянию.");
                continue;
            }
            if (own.Length == 0 || lpn.OriginalReceivingLocationId is not Guid receivingId)
                return OperationError.Conflict("История приёма LPN неполна.");
            if (lpn.Status is not (LicensePlateNumberStatus.Closed
                    or LicensePlateNumberStatus.Deleted))
                return OperationError.Conflict("Состояние закрытого LPN некорректно.");
            var active = lpn.Items.Where(x => !x.IsDeleted).Take(2).ToArray();
            if (active.Length != 1 || order.Items.All(x =>
                    x.StockKeepingUnitId != active[0].StockKeepingUnitId))
                return OperationError.Conflict("Состав LPN не соответствует приходному ордеру.");
            foreach (var movement in own)
            {
                var atReceiving = movement.SourceStorageLocationId is null
                        && movement.DestinationStorageLocationId == receivingId
                    || movement.SourceStorageLocationId == receivingId
                        && movement.DestinationStorageLocationId is null;
                if (movement.PostedAtUtc is null
                    || movement.RecorderType != RecorderType.ReceivingOrder
                    || movement.RecorderId != order.Id || movement.WarehouseId != order.WarehouseId
                    || movement.StockKeepingUnitId != active[0].StockKeepingUnitId
                    || movement.ExpirationDate != active[0].ExpirationDate
                    || !atReceiving)
                    return OperationError.Conflict("История движений не соответствует LPN.");
                var isReversed = reversalByOriginal.ContainsKey(movement.Id);
                if ((lpn.Status == LicensePlateNumberStatus.Deleted) != isReversed)
                    return OperationError.Conflict("История удаления LPN неполна.");
            }
        }
        if (originals.Any(x => x.LicensePlateNumberId is not Guid id
            || !lpnById.ContainsKey(id)))
            return OperationError.Conflict("Движение ордера не связано с его LPN.");

        var toReverse = originals.Where(x => !reversalByOriginal.ContainsKey(x.Id)).ToArray();
        var locations = state.Locations.ToDictionary(x => x.Id);
        var reversals = toReverse.Select(x => new ReceivingLpnReversalPreview(
            x.Id, x.DestinationStorageLocationId,
            x.DestinationStorageLocationId is Guid source
                ? locations.GetValueOrDefault(source)?.DisplayAddress : null,
            x.SourceStorageLocationId,
            x.SourceStorageLocationId is Guid destination
                ? locations.GetValueOrDefault(destination)?.DisplayAddress : null,
            x.StockKeepingUnitId, x.ExpirationDate, x.Quantity)).ToArray();
        var keys = reversals.SelectMany(x => new[]
            { x.SourceStorageLocationId, x.DestinationStorageLocationId }
            .OfType<Guid>().Select(locationId => new StockKey(
                order.WarehouseId, locationId, x.StockKeepingUnitId, x.ExpirationDate)))
            .Distinct().OrderBy(x => x.WarehouseId).ThenBy(x => x.LocationId)
            .ThenBy(x => x.SkuId).ThenBy(x => x.ExpirationDate).ToArray();
        if (keys.Any(x => !locations.ContainsKey(x.LocationId)))
            return OperationError.Conflict("Исходное место движения LPN не найдено.");
        var balances = state.Balances.ToDictionary(x => new StockKey(
            x.WarehouseId, x.StorageLocationId, x.StockKeepingUnitId, x.ExpirationDate));
        var predicted = keys.ToDictionary(x => x,
            x => balances.GetValueOrDefault(x)?.Quantity ?? 0m);
        foreach (var reversal in reversals)
        {
            if (reversal.SourceStorageLocationId is Guid source
                && !Apply(new StockKey(order.WarehouseId, source,
                    reversal.StockKeepingUnitId, reversal.ExpirationDate), -reversal.Quantity))
                return OperationError.Conflict("Прогноз удаления выходит за диапазон остатка.");
            if (reversal.DestinationStorageLocationId is Guid destination
                && !Apply(new StockKey(order.WarehouseId, destination,
                    reversal.StockKeepingUnitId, reversal.ExpirationDate), reversal.Quantity))
                return OperationError.Conflict("Прогноз удаления выходит за диапазон остатка.");
        }

        var stock = keys.Select(key => new ReceivingLpnBalancePreview(
            key.WarehouseId, key.LocationId,
            locations[key.LocationId].DisplayAddress,
            key.SkuId, key.ExpirationDate,
            balances.GetValueOrDefault(key)?.Quantity ?? 0m, predicted[key])).ToArray();
        var token = CommandExecutor.ComputeHash(JsonSerializer.Serialize(new
        {
            order.Id, order.Status, order.OperationalRevision,
            Links = order.SourceLinks.Where(x => x.DetachedAtUtc is null)
                .OrderBy(x => x.Id).Select(x => new
                { x.Id, x.ReceivingSourceDocumentId, x.AttachedAtUtc }),
            Lpns = lpns.Select(x => new
            {
                x.Id, x.Status, x.OperationalRevision, x.ClosedAtUtc,
                x.OriginalReceivingLocationId,
                Items = x.Items.OrderBy(item => item.Id).Select(item => new
                { item.Id, item.StockKeepingUnitId, item.ExpirationDate,
                    item.Quantity, item.IsDeleted })
            }),
            Movements = state.Movements.OrderBy(x => x.Id).Select(x => new
            {
                x.Id, x.LicensePlateNumberId, x.ReversedMovementId,
                x.SourceStorageLocationId, x.DestinationStorageLocationId,
                x.StockKeepingUnitId, x.ExpirationDate, x.Quantity, x.PostedAtUtc
            }),
            ZeroConfirmedSkuIds = state.ZeroConfirmedSkuIds.Order(),
            Balances = keys.Select(key => new
            {
                key.WarehouseId, key.LocationId, key.SkuId, key.ExpirationDate,
                Id = balances.GetValueOrDefault(key)?.Id,
                Quantity = balances.GetValueOrDefault(key)?.Quantity,
                RowVersion = balances.GetValueOrDefault(key) is { } balance
                    ? Convert.ToBase64String(balance.RowVersion) : null
            })
        }));
        return new ReceivingOrderDeletionPreview(order.Id, order.Number, token,
            lpns.Select(x => new ReceivingOrderDeletionLpnPreview(
                x.Id, x.Code, x.Status)).ToArray(),
            reversals, stock, stock.Any(x => x.After < 0));

        bool Apply(StockKey key, decimal delta)
        {
            decimal next;
            try { next = checked(predicted[key] + delta); }
            catch (OverflowException) { return false; }
            if (!WarehouseQuantity.IsSupported(next)) return false;
            predicted[key] = next;
            return true;
        }
    }
}

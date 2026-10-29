using System.Text.Json;
using Wms.Application.Authorization;
using Wms.Application.CommandExecution;
using Wms.Common;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Application.Receiving;

public sealed record ReceivingLpnReversalPreview(
    Guid OriginalMovementId, Guid? SourceStorageLocationId,
    string? SourceAddress, Guid? DestinationStorageLocationId,
    string? DestinationAddress, Guid StockKeepingUnitId,
    DateOnly? ExpirationDate, decimal Quantity);

public sealed record ReceivingLpnBalancePreview(
    Guid WarehouseId, Guid StorageLocationId, string Address, Guid StockKeepingUnitId,
    DateOnly? ExpirationDate, decimal Before, decimal After);

public sealed record ReceivingLpnDeletionPreview(
    Guid OrderId, Guid LpnId, string SnapshotToken,
    IReadOnlyList<ReceivingLpnReversalPreview> Reversals,
    IReadOnlyList<ReceivingLpnBalancePreview> Balances,
    bool RequiresNegativeConfirmation);

public sealed record ReceivingLpnDeletionState(
    ReceivingOrder Order, LicensePlateNumber Lpn,
    IReadOnlyList<InventoryMovement> Movements,
    IReadOnlyList<InventoryBalance> Balances,
    IReadOnlyList<StorageLocation> Locations,
    bool ZeroPreviouslyConfirmed);

public interface IReceivingLpnDeletionStore
{
    Task<ReceivingLpnDeletionState?> LoadPreviewAsync(
        Guid orderId, Guid lpnId, CancellationToken ct);
    Task<ReceivingLpnDeletionState?> LoadForDeletionAsync(
        ICommandExecutionContext session, Guid orderId, Guid lpnId, CancellationToken ct);
}

public sealed class PreviewReceivingLpnDeletion(
    IReceivingLpnDeletionStore store, WarehousePermission permission)
{
    public async Task<OperationResult<ReceivingLpnDeletionPreview>> ExecuteAsync(
        Guid orderId, Guid lpnId, string userId, CancellationToken ct = default)
    {
        if (orderId == Guid.Empty || lpnId == Guid.Empty)
            return OperationError.Invalid("Для предпросмотра нужны ордер и LPN.");
        var access = await permission.RequireManagerAsync(userId, ct);
        if (!access.IsSuccess)
            return access.Error!;
        var state = await store.LoadPreviewAsync(orderId, lpnId, ct);
        return state is null
            ? OperationError.NotFound("Закрытый LPN не найден в ордере.")
            : ReceivingLpnDeletionPreviewFactory.Create(state);
    }
}

internal static class ReceivingLpnDeletionPreviewFactory
{
    private readonly record struct StockKey(
        Guid WarehouseId, Guid LocationId, Guid SkuId, DateOnly? ExpirationDate);

    public static OperationResult<ReceivingLpnDeletionPreview> Create(
        ReceivingLpnDeletionState state)
    {
        var order = state.Order;
        var lpn = state.Lpn;
        if (order.Status == ReceivingOrderStatus.Deleted)
            return OperationError.Conflict("Приходный ордер уже удалён.");
        if (lpn.ReceivingOrderId != order.Id || lpn.Status != LicensePlateNumberStatus.Closed
            || lpn.ReceivingCycleId is null || lpn.OriginalReceivingLocationId is not Guid receivingId)
            return OperationError.Conflict("Удалить можно только закрытый приёмный LPN.");

        var active = lpn.Items.Where(x => !x.IsDeleted).Take(2).ToArray();
        if (active.Length != 1 || order.Items.All(x =>
                x.StockKeepingUnitId != active[0].StockKeepingUnitId))
            return OperationError.Conflict("Состав LPN не соответствует приходному ордеру.");

        var originals = state.Movements.Where(x => x.ReversedMovementId is null)
            .OrderByDescending(x => x.PostedAtUtc).ThenByDescending(x => x.Id).ToArray();
        if (originals.Length == 0 || state.Movements.Any(x =>
                x.ReversedMovementId is not null))
            return OperationError.Conflict("История движений LPN неполна или уже обращена.");
        var putawayIds = lpn.Putaways.Select(x => x.InventoryMovementId).ToHashSet();
        if (putawayIds.Any(id => originals.All(x => x.Id != id)))
            return OperationError.Conflict("История размещений LPN неполна.");

        foreach (var movement in originals)
        {
            var isPutaway = movement.SourceStorageLocationId == receivingId
                && movement.DestinationStorageLocationId is Guid destination
                && destination != receivingId && putawayIds.Contains(movement.Id);
            var isReceiptOrCorrection = !putawayIds.Contains(movement.Id)
                && ((movement.SourceStorageLocationId is null
                        && movement.DestinationStorageLocationId == receivingId)
                    || (movement.SourceStorageLocationId == receivingId
                        && movement.DestinationStorageLocationId is null));
            if (movement.PostedAtUtc is null || movement.LicensePlateNumberId != lpn.Id
                || movement.RecorderType != RecorderType.ReceivingOrder
                || movement.RecorderId != order.Id || movement.WarehouseId != order.WarehouseId
                || movement.StockKeepingUnitId != active[0].StockKeepingUnitId
                || movement.ExpirationDate != active[0].ExpirationDate
                || !(isPutaway || isReceiptOrCorrection))
                return OperationError.Conflict("История движений не соответствует исходным данным LPN.");
        }

        var locations = state.Locations.ToDictionary(x => x.Id);
        var reversals = originals.Select(x => new ReceivingLpnReversalPreview(
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
            return OperationError.Conflict("Исходное место одного из движений LPN не найдено.");
        var balances = state.Balances.ToDictionary(x => new StockKey(
            x.WarehouseId, x.StorageLocationId, x.StockKeepingUnitId, x.ExpirationDate));
        var predicted = keys.ToDictionary(x => x,
            x => balances.GetValueOrDefault(x)?.Quantity ?? 0m);
        foreach (var reversal in reversals)
        {
            if (reversal.SourceStorageLocationId is Guid source
                && !Apply(new StockKey(order.WarehouseId, source,
                    reversal.StockKeepingUnitId, reversal.ExpirationDate), -reversal.Quantity))
                return OperationError.Conflict("Прогноз удаления выходит за диапазон складского остатка.");
            if (reversal.DestinationStorageLocationId is Guid destination
                && !Apply(new StockKey(order.WarehouseId, destination,
                    reversal.StockKeepingUnitId, reversal.ExpirationDate), reversal.Quantity))
                return OperationError.Conflict("Прогноз удаления выходит за диапазон складского остатка.");
        }

        var stock = keys.Select(key => new ReceivingLpnBalancePreview(
            key.WarehouseId, key.LocationId,
            locations.GetValueOrDefault(key.LocationId)?.DisplayAddress ?? key.LocationId.ToString(),
            key.SkuId, key.ExpirationDate,
            balances.GetValueOrDefault(key)?.Quantity ?? 0m, predicted[key])).ToArray();
        var token = CommandExecutor.ComputeHash(JsonSerializer.Serialize(new
        {
            OrderId = order.Id,
            LpnId = lpn.Id, LpnRevision = lpn.OperationalRevision,
            ItemId = active[0].Id, active[0].Quantity, active[0].ExpirationDate,
            state.ZeroPreviouslyConfirmed,
            Movements = originals.Select(x => new
            {
                x.Id, x.SourceStorageLocationId, x.DestinationStorageLocationId,
                x.StockKeepingUnitId, x.ExpirationDate, x.Quantity, x.PostedAtUtc
            }),
            Balances = keys.Select(key => new
            {
                key.WarehouseId, key.LocationId, key.SkuId, key.ExpirationDate,
                Id = balances.GetValueOrDefault(key)?.Id,
                Quantity = balances.GetValueOrDefault(key)?.Quantity,
                RowVersion = balances.GetValueOrDefault(key) is { } balance
                    ? Convert.ToBase64String(balance.RowVersion) : null
            })
        }));
        return new ReceivingLpnDeletionPreview(order.Id, lpn.Id, token,
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

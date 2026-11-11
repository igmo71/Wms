using System.Text.Json;
using System.Globalization;
using Wms.Application.Authorization;
using Wms.Application.CommandExecution;
using Wms.Application.Inventory;
using Wms.Common;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Application.Picking;

public sealed record ConfirmPickRequest(Guid RequestId, Guid PickingAssemblyId,
    long ExpectedRevision, string ScannedLocationCode, Guid StockKeepingUnitId,
    DateOnly? ExpirationDate, decimal Quantity, string UserId,
    Guid? ScannedSkuId = null);

public sealed class ConfirmPick(CommandExecutor executor, IPickingAssemblyStore assemblies,
    IStockAvailabilityStore stock, IStockReservationStore reservations,
    WarehousePermission permission, TimeProvider clock)
{
    private const string CommandType = "picking.pick.confirm";

    public Task<OperationResult<Guid>> ExecuteAsync(ConfirmPickRequest request,
        CancellationToken ct = default)
    {
        if (request is null || request.RequestId == Guid.Empty
            || request.PickingAssemblyId == Guid.Empty
            || request.StockKeepingUnitId == Guid.Empty || request.ExpectedRevision < 0
            || !WarehouseQuantity.IsPositive(request.Quantity)
            || string.IsNullOrWhiteSpace(request.UserId))
            return Task.FromResult<OperationResult<Guid>>(
                OperationError.Invalid("Для Отбора нужны сборка, ячейка, SKU и положительное количество."));
        var normalized = StorageLocationAddress.Normalize(request.ScannedLocationCode);
        if (!normalized.IsSuccess)
            return Task.FromResult<OperationResult<Guid>>(normalized.Error!);
        var code = normalized.Value!;
        var hash = CommandExecutor.ComputeHash(
            $"{request.PickingAssemblyId:N}:{request.ExpectedRevision}:{code}:" +
            $"{request.StockKeepingUnitId:N}:{request.ExpirationDate:yyyy-MM-dd}:" +
            $"{request.Quantity.ToString(CultureInfo.InvariantCulture)}:{request.ScannedSkuId:N}");
        return executor.ExecuteAsync(CommandType, request.RequestId, hash, request.UserId,
            async (session, token) =>
            {
                var access = await permission.RequireOperatorAsync(request.UserId, token);
                if (!access.IsSuccess) return access.Error!;
                var state = await assemblies.LoadWorkAsync(session,
                    request.PickingAssemblyId, code, token);
                var assembly = state.Assembly;
                if (assembly is null) return OperationError.NotFound("Сборка WMS не найдена.");
                var changed = assembly.RecordPhysicalWork(request.UserId,
                    request.ExpectedRevision);
                if (!changed.IsSuccess) return changed.Error!;
                var availability = await stock.ReadSkuAsync(session, assembly.WarehouseId,
                    request.StockKeepingUnitId, token);
                var checkedSource = PickingSourceRules.Check(state,
                    request.StockKeepingUnitId, code, request.ScannedSkuId,
                    request.ExpirationDate, true, availability,
                    DateOnly.FromDateTime(clock.GetLocalNow().DateTime));
                if (!checkedSource.IsSuccess) return checkedSource.Error!;
                var source = checkedSource.Value!;
                if (request.Quantity > source.SuggestedQuantity)
                    return OperationError.Conflict("Количество превышает оставшуюся потребность или доступный остаток.");
                if (state.Location!.Zone!.Type == ZoneType.Reserve)
                {
                    var hold = state.Location.ActiveLock;
                    if (hold is null || hold.OwnerType != StorageLocationLockOwnerType.PickingAssembly
                        || hold.OwnerId != assembly.Id)
                        return OperationError.Conflict("Сначала просканируйте и удержите весь поддон Хранения.");
                    if (request.Quantity != source.AvailableQuantity)
                        return OperationError.Conflict("С поддона нужно подтвердить всю группу срока.");
                }
                var now = clock.GetUtcNow();
                var entry = PickingEntry.Create(assembly.Id, assembly.WarehouseId,
                    source.StorageLocationId, request.StockKeepingUnitId,
                    request.ExpirationDate, request.Quantity, request.UserId, now);
                if (!entry.IsSuccess) return entry.Error!;
                var reservation = PickingReservation.Create(entry.Value!.Id,
                    assembly.Id, assembly.WarehouseId, source.StorageLocationId,
                    request.StockKeepingUnitId, request.ExpirationDate,
                    request.Quantity, request.UserId, now);
                if (!reservation.IsSuccess) return reservation.Error!;
                var reserved = await reservations.ReserveAsync(session,
                    reservation.Value!, token);
                if (!reserved.IsSuccess) return reserved.Error!;
                session.Stage(entry.Value!);
                var audit = WarehouseAuditEvent.Create("picking.pick.confirmed",
                    now, request.UserId, null, null, null,
                    request.StockKeepingUnitId, null, null,
                    JsonSerializer.Serialize(new { EntryId = entry.Value!.Id,
                        source.StorageLocationId, request.ExpirationDate, request.Quantity }),
                    commandUserId: request.UserId, commandType: CommandType,
                    requestId: request.RequestId, pickingAssemblyId: assembly.Id);
                if (!audit.IsSuccess) return audit.Error!;
                return new CommandExecutionResult(entry.Value!.Id, [audit.Value!]);
            }, ct);
    }
}

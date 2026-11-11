using System.Globalization;
using System.Text.Json;
using Wms.Application.Authorization;
using Wms.Application.CommandExecution;
using Wms.Application.Inventory;
using Wms.Common;
using Wms.Domain;

namespace Wms.Application.Picking;

public sealed record ReducePickRequest(Guid RequestId, Guid PickingAssemblyId,
    Guid PickingEntryId, long ExpectedRevision, string ScannedLocationCode,
    decimal Quantity, bool PhysicalReturnConfirmed, string UserId,
    string? Explanation = null);

public sealed class ReducePick(CommandExecutor executor, IPickingAssemblyStore assemblies,
    IStockReservationStore reservations, WarehousePermission permission,
    TimeProvider clock)
{
    private const string CommandType = "picking.pick.reduce";

    public Task<OperationResult<Guid>> ExecuteAsync(ReducePickRequest request,
        CancellationToken ct = default)
    {
        if (request is null || request.RequestId == Guid.Empty
            || request.PickingAssemblyId == Guid.Empty || request.PickingEntryId == Guid.Empty
            || request.ExpectedRevision < 0 || !WarehouseQuantity.IsPositive(request.Quantity)
            || !request.PhysicalReturnConfirmed || string.IsNullOrWhiteSpace(request.UserId)
            || request.Explanation?.Length > 500)
            return Task.FromResult<OperationResult<Guid>>(
                OperationError.Invalid("Укажите запись, положительное количество и подтвердите физический возврат."));
        var normalized = StorageLocationAddress.Normalize(request.ScannedLocationCode);
        if (!normalized.IsSuccess)
            return Task.FromResult<OperationResult<Guid>>(normalized.Error!);
        var code = normalized.Value!;
        var hash = CommandExecutor.ComputeHash(
            $"{request.PickingAssemblyId:N}:{request.PickingEntryId:N}:" +
            $"{request.ExpectedRevision}:{code}:" +
            $"{request.Quantity.ToString(CultureInfo.InvariantCulture)}:" +
            request.Explanation?.Trim());
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
                var entry = state.Entries.SingleOrDefault(x => x.Id == request.PickingEntryId);
                if (entry is null)
                    return OperationError.NotFound("Запись Отбора не найдена в этой сборке.");
                if (state.Location is null || state.Location.Id != entry.StorageLocationId)
                    return OperationError.Conflict("Сканируйте исходную ячейку записи Отбора.");
                if (request.Quantity > entry.Quantity)
                    return OperationError.Conflict("Возвращаемое количество превышает подтверждённый факт.");
                var before = entry.Quantity;
                var now = clock.GetUtcNow();
                var reduced = entry.Reduce(request.Quantity, request.UserId, now);
                if (!reduced.IsSuccess) return reduced.Error!;
                var released = await reservations.ReduceAsync(session, entry.Id,
                    request.Quantity, now, token);
                if (!released.IsSuccess) return released.Error!;
                var audit = WarehouseAuditEvent.Create("picking.pick.reduced",
                    now, request.UserId, null, null, null,
                    entry.StockKeepingUnitId, null,
                    JsonSerializer.Serialize(new { EntryId = entry.Id, Quantity = before }),
                    JsonSerializer.Serialize(new { EntryId = entry.Id, entry.Quantity,
                        Returned = request.Quantity, entry.StorageLocationId,
                        entry.ExpirationDate, Explanation = request.Explanation?.Trim() }),
                    commandUserId: request.UserId, commandType: CommandType,
                    requestId: request.RequestId, pickingAssemblyId: assembly.Id);
                if (!audit.IsSuccess) return audit.Error!;
                return new CommandExecutionResult(entry.Id, [audit.Value!]);
            }, ct);
    }
}

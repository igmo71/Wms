using System.Text.Json;
using Wms.Application.Authorization;
using Wms.Application.CommandExecution;
using Wms.Application.Inventory;
using Wms.Common;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Application.Picking;

public sealed record HoldPickingPalletRequest(Guid RequestId, Guid PickingAssemblyId,
    long ExpectedRevision, string LocationCode, Guid StockKeepingUnitId, string UserId);

public sealed class HoldPickingPallet(CommandExecutor executor, IPickingAssemblyStore assemblies,
    IStockAvailabilityStore stock, IStockReservationStore reservations,
    WarehousePermission permission, TimeProvider clock)
{
    private const string CommandType = "picking.pallet.hold";

    public Task<OperationResult<Guid>> ExecuteAsync(HoldPickingPalletRequest request,
        CancellationToken ct = default)
    {
        if (request is null || request.RequestId == Guid.Empty
            || request.PickingAssemblyId == Guid.Empty || request.StockKeepingUnitId == Guid.Empty
            || request.ExpectedRevision < 0 || string.IsNullOrWhiteSpace(request.UserId))
            return Task.FromResult<OperationResult<Guid>>(
                OperationError.Invalid("Укажите сборку, ячейку, SKU, ревизию и сотрудника."));
        var normalized = StorageLocationAddress.Normalize(request.LocationCode);
        if (!normalized.IsSuccess)
            return Task.FromResult<OperationResult<Guid>>(normalized.Error!);
        var code = normalized.Value!;
        var hash = CommandExecutor.ComputeHash(
            $"{request.PickingAssemblyId:N}:{request.ExpectedRevision}:{code}:{request.StockKeepingUnitId:N}");
        return executor.ExecuteAsync(CommandType, request.RequestId, hash, request.UserId,
            async (session, token) =>
            {
                var access = await permission.RequireOperatorAsync(request.UserId, token);
                if (!access.IsSuccess) return access.Error!;
                var state = await assemblies.LoadWorkAsync(session, request.PickingAssemblyId,
                    code, token);
                var assembly = state.Assembly;
                if (assembly is null) return OperationError.NotFound("Сборка WMS не найдена.");
                var changed = assembly.RecordPhysicalWork(request.UserId,
                    request.ExpectedRevision);
                if (!changed.IsSuccess) return changed.Error!;
                var location = state.Location;
                if (location is null || location.IsFolder || location.DeletionMark
                    || location.Warehouse?.DeletionMark != false
                    || location.Zone is null || location.Zone.DeletionMark
                    || location.Zone.WarehouseId != assembly.WarehouseId
                    || location.Zone.Type != ZoneType.Reserve)
                    return OperationError.Conflict("Для прямого палетного Отбора нужна активная ячейка Хранения.");
                if (state.CellSkus.Count != 1 || state.CellSkus[0].Id != request.StockKeepingUnitId)
                    return OperationError.Conflict("Прямой палетный Отбор допускает ровно один SKU в ячейке.");
                var available = await stock.ReadSkuAsync(session, assembly.WarehouseId,
                    request.StockKeepingUnitId, token);
                var cell = available.Where(x => x.Group.StorageLocationId == location.Id
                    && x.AccountingQuantity > 0).ToArray();
                var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
                if (cell.Length == 0 || cell.Any(x =>
                    x.Group.ExpirationDate < today
                    || x.AccountingQuantity != x.AvailableForNewPick(assembly.Id)))
                    return OperationError.Conflict("Поддон нельзя полностью отобрать: срок, резерв или блокировка.");
                var remaining = assembly.PlanLines.Where(x =>
                        x.StockKeepingUnitId == request.StockKeepingUnitId).Sum(x => x.Quantity)
                    - state.Entries.Where(x => x.StockKeepingUnitId == request.StockKeepingUnitId)
                        .Sum(x => x.Quantity);
                if (cell.Sum(x => x.AccountingQuantity) > remaining)
                    return OperationError.Conflict("Остаток поддона превышает потребность сборки.");
                var latest = cell.OrderBy(x => x.Group.ExpirationDate is null)
                    .ThenBy(x => x.Group.ExpirationDate).Last().Group.ExpirationDate;
                if (available.Any(x => x.Group.StorageLocationId != location.Id
                    && x.AvailableForNewPick(assembly.Id) > 0
                    && (x.Group.ExpirationDate is null || x.Group.ExpirationDate >= today)
                    && (x.Group.ExpirationDate is not null
                        && (latest is null || x.Group.ExpirationDate < latest))))
                    return OperationError.Conflict("Прямой палетный Отбор нарушит очередность сроков.");
                var hold = StorageLocationLock.CreateForPicking(location.Id,
                    assembly.Id, clock.GetUtcNow(), request.UserId);
                if (!hold.IsSuccess) return hold.Error!;
                var held = await reservations.HoldAsync(session, assembly.WarehouseId,
                    hold.Value!, token);
                if (!held.IsSuccess) return held.Error!;
                var audit = WarehouseAuditEvent.Create("picking.pallet.held",
                    clock.GetUtcNow(), request.UserId, null, null, null,
                    request.StockKeepingUnitId, null, null,
                    JsonSerializer.Serialize(new { LocationId = location.Id,
                        Quantity = cell.Sum(x => x.AccountingQuantity) }),
                    commandUserId: request.UserId, commandType: CommandType,
                    requestId: request.RequestId, pickingAssemblyId: assembly.Id);
                if (!audit.IsSuccess) return audit.Error!;
                return new CommandExecutionResult(location.Id, [audit.Value!]);
            }, ct);
    }
}

using System.Globalization;
using System.Text.Json;
using Wms.Application.Authorization;
using Wms.Application.CommandExecution;
using Wms.Application.Inventory;
using Wms.Common;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Application.Picking;

public sealed record ReportPickingShortageRequest(
    Guid RequestId, Guid PickingAssemblyId, long ExpectedRevision,
    string ScannedLocationCode, Guid StockKeepingUnitId, DateOnly? ExpirationDate,
    decimal ExpectedQuantity, decimal FoundQuantity, string UserId,
    Guid? ExistingPickingEntryId = null, string? Comment = null);

public sealed class ReportPickingShortage(CommandExecutor executor,
    IPickingAssemblyStore assemblies, IStockAvailabilityStore stock,
    IStockReservationStore reservations, IStockShortageStore shortages,
    WarehousePermission permission, TimeProvider clock)
{
    private const string CommandType = "picking.shortage.report";

    public Task<OperationResult<Guid>> ExecuteAsync(
        ReportPickingShortageRequest request, CancellationToken ct = default)
    {
        if (request is null || request.RequestId == Guid.Empty
            || request.PickingAssemblyId == Guid.Empty || request.StockKeepingUnitId == Guid.Empty
            || request.ExpectedRevision < 0
            || !WarehouseQuantity.IsPositive(request.ExpectedQuantity)
            || !WarehouseQuantity.IsNonNegative(request.FoundQuantity)
            || request.FoundQuantity >= request.ExpectedQuantity
            || request.ExistingPickingEntryId == Guid.Empty
            || request.Comment?.Trim().Length > StockShortage.MaximumCommentLength
            || string.IsNullOrWhiteSpace(request.UserId))
            return Task.FromResult<OperationResult<Guid>>(
                OperationError.Invalid("Укажите ячейку, SKU, ожидаемое и меньшее найденное количество."));
        var normalized = StorageLocationAddress.Normalize(request.ScannedLocationCode);
        if (!normalized.IsSuccess)
            return Task.FromResult<OperationResult<Guid>>(normalized.Error!);
        var code = normalized.Value!;
        var hash = CommandExecutor.ComputeHash(string.Join(":", new[]
        {
            request.PickingAssemblyId.ToString("N"),
            request.ExpectedRevision.ToString(CultureInfo.InvariantCulture), code,
            request.StockKeepingUnitId.ToString("N"),
            request.ExpirationDate?.ToString("yyyy-MM-dd") ?? "",
            request.ExpectedQuantity.ToString(CultureInfo.InvariantCulture),
            request.FoundQuantity.ToString(CultureInfo.InvariantCulture),
            request.ExistingPickingEntryId?.ToString("N") ?? "",
            request.Comment?.Trim() ?? ""
        }));
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
                    request.StockKeepingUnitId, code, null,
                    request.ExpirationDate, true, availability,
                    DateOnly.FromDateTime(clock.GetLocalNow().DateTime));
                if (!checkedSource.IsSuccess) return checkedSource.Error!;
                var source = checkedSource.Value!;
                if (state.Location!.Zone!.Type == ZoneType.Reserve
                    && (state.Location.ActiveLock?.OwnerType != StorageLocationLockOwnerType.PickingAssembly
                        || state.Location.ActiveLock.OwnerId != assembly.Id))
                    return OperationError.Conflict("Перед сообщением о недостаче на поддоне удержите ячейку.");
                var missing = request.ExpectedQuantity - request.FoundQuantity;
                if (missing > source.SuggestedQuantity)
                    return OperationError.Conflict("Спорное количество превышает свободный остаток или потребность.");

                var existing = request.ExistingPickingEntryId is Guid entryId
                    ? state.Entries.SingleOrDefault(x => x.Id == entryId) : null;
                if (request.ExistingPickingEntryId is not null
                    && (existing is null || existing.Quantity <= 0
                        || existing.WarehouseId != assembly.WarehouseId
                        || existing.StorageLocationId != source.StorageLocationId
                        || existing.StockKeepingUnitId != request.StockKeepingUnitId
                        || existing.ExpirationDate != request.ExpirationDate
                        || existing.Quantity != request.FoundQuantity))
                    return OperationError.Conflict("Ранее подтверждённый факт не совпадает с найденным количеством.");
                if (existing is null && request.FoundQuantity > source.SuggestedQuantity - missing)
                    return OperationError.Conflict("Найденное количество превышает свободный остаток или план.");

                var now = clock.GetUtcNow();
                PickingEntry? newEntry = null;
                if (existing is null && request.FoundQuantity > 0)
                {
                    var created = PickingEntry.Create(assembly.Id, assembly.WarehouseId,
                        source.StorageLocationId, request.StockKeepingUnitId,
                        request.ExpirationDate, request.FoundQuantity, request.UserId, now);
                    if (!created.IsSuccess) return created.Error!;
                    newEntry = created.Value!;
                    var reservation = PickingReservation.Create(newEntry.Id,
                        assembly.Id, assembly.WarehouseId, source.StorageLocationId,
                        request.StockKeepingUnitId, request.ExpirationDate,
                        request.FoundQuantity, request.UserId, now);
                    if (!reservation.IsSuccess) return reservation.Error!;
                    var reserved = await reservations.ReserveAsync(session,
                        reservation.Value!, token);
                    if (!reserved.IsSuccess) return reserved.Error!;
                    session.Stage(newEntry);
                }
                var shortage = StockShortage.Report(Guid.NewGuid(), assembly.WarehouseId,
                    source.StorageLocationId, request.StockKeepingUnitId,
                    request.ExpirationDate, StockShortageOrigin.Picking, assembly.Id,
                    request.ExpectedQuantity, request.FoundQuantity, request.Comment,
                    request.UserId, now);
                if (!shortage.IsSuccess) return shortage.Error!;
                var blocked = await shortages.ReportAsync(session, shortage.Value!, token);
                if (!blocked.IsSuccess) return blocked.Error!;
                var audit = WarehouseAuditEvent.Create("picking.shortage.reported",
                    now, request.UserId, null, null, null,
                    request.StockKeepingUnitId, null, null,
                    JsonSerializer.Serialize(new { ShortageId = shortage.Value!.Id,
                        source.StorageLocationId, request.ExpirationDate,
                        request.ExpectedQuantity, request.FoundQuantity,
                        PickingEntryId = existing?.Id ?? newEntry?.Id,
                        Comment = request.Comment?.Trim() }),
                    commandUserId: request.UserId, commandType: CommandType,
                    requestId: request.RequestId, pickingAssemblyId: assembly.Id);
                if (!audit.IsSuccess) return audit.Error!;
                return new CommandExecutionResult(shortage.Value!.Id, [audit.Value!]);
            }, ct);
    }
}

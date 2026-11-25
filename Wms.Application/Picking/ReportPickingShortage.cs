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
    string? Comment = null);

public sealed class ReportPickingShortage(CommandExecutor executor,
    IPickingAssemblyStore assemblies, IStockAvailabilityStore stock,
    IStockReservationStore reservations, IStockShortageStore shortages,
    WarehousePermission permission, AdjustPickingRouteStock routeStock, TimeProvider clock)
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
            request.Comment?.Trim() ?? ""
        }));
        return executor.ExecuteAsync(CommandType, request.RequestId, hash, request.UserId,
            async (session, token) =>
            {
                var access = await permission.RequireOperatorAsync(request.UserId, token);
                if (!access.IsSuccess) return access.Error!;
                var loaded = await assemblies.LoadStockChangeAsync(session,
                    request.PickingAssemblyId, code, request.StockKeepingUnitId, token);
                if (!loaded.IsSuccess) return loaded.Error!;
                var stockChange = loaded.Value!;
                var state = stockChange.Work;
                var assembly = state.Assembly;
                if (assembly is null) return OperationError.NotFound("Сборка WMS не найдена.");
                var changed = assembly.RecordPhysicalWork(request.UserId,
                    request.ExpectedRevision);
                if (!changed.IsSuccess) return changed.Error!;
                var availability = await stock.ReadSkuAsync(session, assembly.WarehouseId,
                    request.StockKeepingUnitId, token);
                var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
                var checkedSource = PickingSourceRules.Check(state,
                    request.StockKeepingUnitId, code, null,
                    request.ExpirationDate, true, availability, today);
                if (!checkedSource.IsSuccess) return checkedSource.Error!;
                var source = checkedSource.Value!;
                if (state.Location!.Zone!.Type == ZoneType.Reserve
                    && (state.Location.ActiveLock?.OwnerType != StorageLocationLockOwnerType.PickingAssembly
                        || state.Location.ActiveLock.OwnerId != assembly.Id))
                    return OperationError.Conflict("Перед сообщением о недостаче на поддоне удержите ячейку.");
                if (request.ExpectedQuantity != source.ExpectedPhysicalQuantity
                    || request.FoundQuantity >= source.SuggestedQuantity)
                    return OperationError.Conflict(
                        "Остаток или потребность изменились. Повторно сканируйте ячейку перед подтверждением недостачи.");

                var now = clock.GetUtcNow();
                var released = routeStock.ReleaseShortGroup(session, stockChange, source, request.FoundQuantity);
                if (!released.IsSuccess) return released.Error!;
                PickingEntry? newEntry = null;
                if (request.FoundQuantity > 0)
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
                    request.ExpirationDate, assembly.Id,
                    request.ExpectedQuantity, request.FoundQuantity, request.Comment,
                    request.UserId, now);
                if (!shortage.IsSuccess) return shortage.Error!;
                var blocked = await shortages.ReportAsync(session, shortage.Value!, token);
                if (!blocked.IsSuccess) return blocked.Error!;

                var quantities = released.Value!;
                var affected = await assemblies.LoadRouteOwnersAsync(session, quantities.Keys.ToArray(),
                    request.StockKeepingUnitId, token);
                var reportingWork = newEntry is null ? state : state with { Entries = [.. state.Entries, newEntry] };
                foreach (var work in affected.Select(x => x.Assembly!.Id == assembly.Id ? reportingWork : x)
                    .OrderBy(x => x.Assembly!.ActiveSources.Select(link =>
                        ShippingQueuePriority.Rank(link.ShippingOrder?.Queue ?? ShippingOrderQueue.Unknown))
                        .DefaultIfEmpty(ShippingQueuePriority.Rank(ShippingOrderQueue.Unknown)).Min())
                    .ThenBy(x => x.Assembly!.StartedAtUtc).ThenBy(x => x.Assembly!.Id))
                {
                    var owner = work.Assembly!;
                    if (owner.Id != assembly.Id)
                    {
                        var adjustment = owner.RecordRouteAdjustment();
                        if (!adjustment.IsSuccess) return adjustment.Error!;
                    }
                    var replacement = await routeStock.ReplaceAsync(session, work, stockChange.Locations,
                        request.StockKeepingUnitId, quantities[owner.Id], today, token);
                    if (!replacement.IsSuccess) return replacement.Error!;
                }
                var audit = WarehouseAuditEvent.Create("picking.shortage.reported",
                    now, request.UserId, null, null, null,
                    request.StockKeepingUnitId, null, null,
                    JsonSerializer.Serialize(new { ShortageId = shortage.Value!.Id,
                        source.StorageLocationId, request.ExpirationDate,
                        request.ExpectedQuantity, request.FoundQuantity,
                        PickingEntryId = newEntry?.Id,
                        AffectedAssemblyIds = affected.Where(x => x.Assembly!.Id != assembly.Id)
                            .Select(x => x.Assembly!.Id).Order().ToArray(),
                        Comment = request.Comment?.Trim() }),
                    commandUserId: request.UserId, commandType: CommandType,
                    requestId: request.RequestId, pickingAssemblyId: assembly.Id);
                if (!audit.IsSuccess) return audit.Error!;
                return new CommandExecutionResult(shortage.Value!.Id, [audit.Value!]);
            }, ct);
    }
}

using System.Text.Json;
using Wms.Application.Authorization;
using Wms.Application.CommandExecution;
using Wms.Common;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Application.Picking;

public sealed record RestoreShippingStatusError(Guid ShippingOrderId, string Error);

public sealed class RestoreShippingSourceStatus(
    IPickingAssemblyStore assemblies, IShippingExchangeStore exchangeStore,
    IShippingSourceReader reader, IShippingStoredSnapshotParser parser,
    IShippingSourceWriter writer, CommandExecutor executor,
    WarehousePermission permission, TimeProvider clock)
{
    public async Task<OperationResult<IReadOnlyList<RestoreShippingStatusError>>> ExecuteAsync(
        Guid pickingAssemblyId, string userId,
        CancellationToken ct = default)
    {
        if (pickingAssemblyId == Guid.Empty
            || string.IsNullOrWhiteSpace(userId) || userId.Length > 36)
            return OperationError.Invalid("Укажите сборку и исполнителя возврата.");
        var access = await permission.RequireManagerAsync(userId, ct);
        if (!access.IsSuccess) return access.Error!;
        var assembly = await assemblies.ReadAssemblyAsync(pickingAssemblyId, ct);
        if (assembly is null)
            return OperationError.NotFound("Сборка WMS не найдена.");

        var errors = new List<RestoreShippingStatusError>();
        if (assembly.Status == PickingAssemblyStatus.Cancelled)
            return errors;
        if (assembly.Status != PickingAssemblyStatus.CancellationPending)
            return OperationError.Conflict("Сборка не ожидает возврата статусов 1С.");

        foreach (var sourceId in assembly.ActiveSources.Select(x => x.ShippingOrderId).Order())
        {
            try
            {
                var result = await RestoreAsync(pickingAssemblyId, sourceId, userId, ct);
                if (!result.IsSuccess)
                    errors.Add(new RestoreShippingStatusError(sourceId, result.Error!.Message));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                errors.Add(new RestoreShippingStatusError(sourceId,
                    $"Возврат статуса РО 1С прерван: {exception.Message}"));
            }
        }
        return errors;
    }

    private async Task<OperationResult> RestoreAsync(
        Guid pickingAssemblyId, Guid shippingOrderId, string userId, CancellationToken ct)
    {
        var acquired = await exchangeStore.AcquireAsync(shippingOrderId, ct);
        if (!acquired.IsSuccess) return acquired.Error!;
        await using var lease = acquired.Value!;

        var assembly = await assemblies.ReadAssemblyAsync(pickingAssemblyId, ct);
        if (assembly?.SourceLinks.Any(x => x.ShippingOrderId == shippingOrderId
            && x.ReturnPostConfirmedAtUtc is not null) == true)
            return OperationResult.Success();
        if (assembly?.Status != PickingAssemblyStatus.CancellationPending)
            return OperationError.Conflict("Сборка не ожидает возврата статусов 1С.");
        var link = assembly.ActiveSources.SingleOrDefault(x =>
            x.ShippingOrderId == shippingOrderId);
        if (link is null || link.StartPatchRequestedAtUtc is null)
            return OperationError.Conflict("WMS не переключала этот РО 1С.");

        var fresh = await reader.ReadAsync(shippingOrderId, ct);
        if (!fresh.IsSuccess)
            return await FailAsync(assembly, shippingOrderId, userId,
                fresh.Error!.Message, ct);
        var current = fresh.Value!;
        var same = parser.UnchangedApartFromStatusAndPosting(shippingOrderId,
            link.PlanSourceSnapshotJson, current);
        if (!same.IsSuccess || same.Value != true)
            return await FailAsync(assembly, shippingOrderId, userId,
                same.Error?.Message ??
                "РО 1С изменился после переключения WMS. Возврат заблокирован.", ct);
        if (current.DeletionMark != false || current.Posted is null)
            return await FailAsync(assembly, shippingOrderId, userId,
                "Пометка удаления или проведение РО 1С не позволяют возврат.", ct);

        if (link.ReturnPatchRequestedAtUtc is null)
        {
            if (current.Status == ShippingOrderStatus.Prepared
                && current.Posted == true && link.StartConfirmedAtUtc is null)
                return await ReleaseAsync(assembly, current,
                    userId, ct);
            if (current.Status != ShippingOrderStatus.ReadyForPicking
                || current.Posted != true)
                return await FailAsync(assembly, shippingOrderId, userId,
                    "РО 1С уже менялся вне WMS. Автоматический возврат запрещён.", ct);
            var intent = await ChangeAsync(assembly, shippingOrderId,
                userId, ReturnChange.Intent, null, ct);
            if (!intent.IsSuccess) return intent.Error!;
            assembly = intent.Value!;
            link = assembly.ActiveSources.Single(x => x.ShippingOrderId == shippingOrderId);
        }

        if (current.Status == ShippingOrderStatus.ReadyForPicking)
        {
            if (link.ReturnPatchConfirmedAtUtc is not null || current.Posted != true)
                return await FailAsync(assembly, shippingOrderId, userId,
                    "Статус РО 1С изменился после возврата. Требуется разбор.", ct);
            var patched = await writer.SetPreparedAsync(shippingOrderId, ct);
            if (!patched.IsSuccess)
                return await FailAsync(assembly, shippingOrderId, userId,
                    patched.Error!.Message, ct);
            fresh = await reader.ReadAsync(shippingOrderId, ct);
            if (!fresh.IsSuccess)
                return await FailAsync(assembly, shippingOrderId, userId,
                    fresh.Error!.Message, ct);
            current = fresh.Value!;
        }
        if (current.Status != ShippingOrderStatus.Prepared
            || current.DeletionMark != false || current.Posted is null)
            return await FailAsync(assembly, shippingOrderId, userId,
                "1С не подтвердила возврат статуса РО 1С.", ct);
        same = parser.UnchangedApartFromStatusAndPosting(shippingOrderId,
            link.PlanSourceSnapshotJson, current);
        if (!same.IsSuccess || same.Value != true)
            return await FailAsync(assembly, shippingOrderId, userId,
                same.Error?.Message ?? "РО 1С изменился во время возврата.", ct);
        if (link.ReturnPatchConfirmedAtUtc is null)
        {
            var confirmed = await ChangeAsync(assembly, shippingOrderId,
                userId, ReturnChange.Patch, null, ct);
            if (!confirmed.IsSuccess) return confirmed.Error!;
            assembly = confirmed.Value!;
        }
        if (current.Posted != true)
        {
            var posted = await writer.PostAsync(shippingOrderId, ct);
            if (!posted.IsSuccess)
                return await FailAsync(assembly, shippingOrderId, userId,
                    posted.Error!.Message, ct);
            fresh = await reader.ReadAsync(shippingOrderId, ct);
            if (!fresh.IsSuccess)
                return await FailAsync(assembly, shippingOrderId, userId,
                    fresh.Error!.Message, ct);
            current = fresh.Value!;
            same = parser.UnchangedApartFromStatusAndPosting(shippingOrderId,
                link.PlanSourceSnapshotJson, current);
            if (!same.IsSuccess || same.Value != true)
                return await FailAsync(assembly, shippingOrderId, userId,
                    same.Error?.Message ?? "РО 1С изменился после POST.", ct);
        }
        if (current.Status != ShippingOrderStatus.Prepared
            || current.Posted != true || current.DeletionMark != false)
            return await FailAsync(assembly, shippingOrderId, userId,
                "1С не подтвердила проведённый РО 1С в статусе Подготовлен.", ct);
        return await ReleaseAsync(assembly, current, userId, ct);
    }

    private async Task<OperationResult> FailAsync(
        PickingAssembly assembly, Guid sourceId, string userId,
        string error, CancellationToken ct)
    {
        var message = string.IsNullOrWhiteSpace(error)
            ? "Неизвестная ошибка возврата РО 1С." : error.Trim();
        if (message.Length > 2000) message = message[..2000];
        var changed = await ChangeAsync(assembly, sourceId, userId,
            ReturnChange.Error, message, ct);
        return changed.IsSuccess ? OperationError.Conflict(message) : changed.Error!;
    }

    private async Task<OperationResult> ReleaseAsync(PickingAssembly assembly,
        ShippingSourceSnapshot snapshot, string userId, CancellationToken ct)
    {
        var changed = await ChangeAsync(assembly, snapshot.RefKey, userId,
            ReturnChange.Release, null, ct, snapshot);
        return changed.IsSuccess ? OperationResult.Success() : changed.Error!;
    }

    private async Task<OperationResult<PickingAssembly>> ChangeAsync(
        PickingAssembly state, Guid sourceId, string userId,
        ReturnChange change, string? error, CancellationToken ct,
        ShippingSourceSnapshot? restoredSource = null)
    {
        var requestId = Guid.NewGuid();
        var commandType = $"picking.source-return.{change.ToString().ToLowerInvariant()}";
        var hash = CommandExecutor.ComputeHash(
            $"{state.Id:N}:{sourceId:N}:{state.OperationalRevision}:{error}");
        var result = await executor.ExecuteAsync(commandType, requestId, hash,
            userId, async (session, operationCt) =>
            {
                PickingCancellationState? cancellation = null;
                if (change == ReturnChange.Release)
                    cancellation = await assemblies.LoadCancellationAsync(session,
                        state.Id, operationCt);
                var loaded = cancellation?.Assembly
                    ?? (change == ReturnChange.Release ? null
                        : await assemblies.LoadAssemblyAsync(session,
                            state.Id, operationCt));
                if (loaded is null)
                    return OperationError.NotFound("Сборка WMS не найдена.");
                var now = clock.GetUtcNow();
                var transition = change switch
                {
                    ReturnChange.Intent => loaded.RecordReturnPatchIntent(sourceId,
                        state.OperationalRevision, now),
                    ReturnChange.Patch => loaded.ConfirmReturnPatch(sourceId,
                        state.OperationalRevision, now),
                    ReturnChange.Error => loaded.RecordReturnFailure(sourceId,
                        state.OperationalRevision, now, error!),
                    _ => loaded.ReleaseCancelledSource(sourceId, userId,
                        state.OperationalRevision, now)
                };
                if (!transition.IsSuccess) return transition.Error!;
                if (restoredSource is not null)
                {
                    var projection = SynchronizeShippingSource.Project(restoredSource);
                    if (!projection.IsSuccess) return projection.Error!;
                    var source = await assemblies.LoadSourceAsync(session, sourceId, operationCt);
                    if (source is null) return OperationError.NotFound("РО 1С не найден в WMS.");
                    var refreshed = source.ApplyCurrentSource(projection.Value.Source, now);
                    if (!refreshed.IsSuccess) return refreshed.Error!;
                    var recorded = source.RecordSourceCheck(restoredSource.RawDocumentJson, null, now);
                    if (!recorded.IsSuccess) return recorded.Error!;

                    if (loaded.ActiveSources.Count == 0 && cancellation!.Replenishments.All(x =>
                        x.Status != ReplenishmentTaskStatus.InProgress))
                    {
                        var completed = loaded.CompleteCancellation(now, false);
                        if (!completed.IsSuccess) return completed.Error!;
                    }
                }
                var audit = WarehouseAuditEvent.Create(commandType, now,
                    userId, null, null, null, null, null,
                    JsonSerializer.Serialize(new { ShippingOrderId = sourceId,
                        Revision = state.OperationalRevision }),
                    JsonSerializer.Serialize(new { ShippingOrderId = sourceId,
                        loaded.OperationalRevision, loaded.Status,
                        Error = error is { Length: > 300 } ? error[..300] : error }),
                    commandUserId: userId, commandType: commandType,
                    requestId: requestId, pickingAssemblyId: state.Id);
                if (!audit.IsSuccess) return audit.Error!;
                return new CommandExecutionResult(state.Id, [audit.Value!]);
            }, ct);
        if (!result.IsSuccess) return result.Error!;
        var refreshed = await assemblies.ReadAssemblyAsync(state.Id, ct);
        return refreshed is null
            ? OperationError.NotFound("Сборка WMS не найдена.")
            : refreshed;
    }

    private enum ReturnChange { Intent, Patch, Error, Release }
}

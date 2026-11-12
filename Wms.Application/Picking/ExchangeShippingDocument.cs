using System.Text.Json;
using Wms.Application.Authorization;
using Wms.Application.CommandExecution;
using Wms.Common;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Application.Picking;

public sealed record ShippingExchangeResult(Guid ShippingOrderId,
    ShippingExchangeStage Stage, string? Error);

public sealed class ExchangeShippingDocument(
    IShippingExchangeStore store, IShippingSourceReader reader,
    IShippingSourceWriter writer, CommandExecutor executor,
    WarehousePermission permission, TimeProvider clock)
{
    public async Task<OperationResult<ShippingExchangeResult>> ExecuteAsync(
        Guid pickingAssemblyId, Guid shippingOrderId, string userId,
        CancellationToken ct = default)
    {
        if (pickingAssemblyId == Guid.Empty || shippingOrderId == Guid.Empty
            || string.IsNullOrWhiteSpace(userId) || userId.Length > 36)
            return OperationError.Invalid("Укажите сборку, РО 1С и исполнителя обмена.");
        var operatorAccess = await permission.RequireOperatorAsync(userId, ct);
        if (!operatorAccess.IsSuccess)
        {
            var managerAccess = await permission.RequireManagerAsync(userId, ct);
            if (!managerAccess.IsSuccess) return operatorAccess.Error!;
        }

        var acquired = await store.AcquireAsync(shippingOrderId, ct);
        if (!acquired.IsSuccess) return acquired.Error!;
        await using var lease = acquired.Value!;
        var state = await store.ReadAsync(pickingAssemblyId, shippingOrderId, ct);
        if (state.Target is null || state.PlanSourceSnapshotJson is null)
            return OperationError.NotFound("Цель обмена РО 1С не найдена.");
        if (state.AssemblyStatus != PickingAssemblyStatus.Closed)
            return OperationError.Conflict("Обмен возможен после складского закрытия сборки.");
        if (state.Target.IsConfirmed || state.Target.Stage == ShippingExchangeStage.NeedsReview)
            return Result(state.Target);

        var fresh = await reader.ReadAsync(shippingOrderId, ct);
        if (!fresh.IsSuccess)
            return await FailAsync(state.Target, userId,
                fresh.Error!.Message, ct);
        var current = fresh.Value!;
        if (current.DeletionMark is null || current.Posted is null)
            return await FailAsync(state.Target, userId,
                "1С не вернула признак удаления или проведения РО 1С.", ct);
        if (current.DeletionMark == true)
            return await ReviewAsync(state.Target, userId,
                "РО 1С помечен на удаление. Восстановите его и примите новое решение.", ct);

        var reached = writer.TargetMatches(state.Target,
            state.PlanSourceSnapshotJson, current);
        if (!reached.IsSuccess)
            return await FailAsync(state.Target, userId, reached.Error!.Message, ct);
        if (reached.Value == true)
        {
            if (state.Target.Stage == ShippingExchangeStage.PendingPatch)
            {
                var confirmed = await ChangeAsync(state.Target, userId,
                    Change.Patch, null, ct);
                if (!confirmed.IsSuccess) return confirmed.Error!;
                state = await store.ReadAsync(pickingAssemblyId, shippingOrderId, ct);
            }
            if (current.Posted == true)
            {
                var confirmed = await ChangeAsync(state.Target!, userId,
                    Change.Post, null, ct);
                return confirmed.IsSuccess ? Result(confirmed.Value!) : confirmed.Error!;
            }
        }
        else
        {
            if (state.Target.Stage == ShippingExchangeStage.PatchConfirmed
                || current.RawDocumentJson != state.Target.ReviewedSourceJson)
                return await ReviewAsync(state.Target, userId,
                    "РО 1С изменился после рассмотренного состояния. Требуется решение Manager.", ct);

            var patched = await writer.ApplyTargetAsync(state.Target,
                state.PlanSourceSnapshotJson, current, ct);
            if (!patched.IsSuccess)
                return await FailAsync(state.Target, userId,
                    patched.Error!.Message, ct);
            fresh = await reader.ReadAsync(shippingOrderId, ct);
            if (!fresh.IsSuccess)
                return await FailAsync(state.Target, userId,
                    fresh.Error!.Message, ct);
            current = fresh.Value!;
            if (current.DeletionMark is null || current.Posted is null)
                return await FailAsync(state.Target, userId,
                    "1С не вернула признак удаления или проведения после PATCH.", ct);
            if (current.DeletionMark == true)
                return await ReviewAsync(state.Target, userId,
                    "РО 1С помечен на удаление после PATCH.", ct);
            reached = writer.TargetMatches(state.Target,
                state.PlanSourceSnapshotJson, current);
            if (!reached.IsSuccess)
                return await FailAsync(state.Target, userId,
                    reached.Error!.Message, ct);
            if (reached.Value != true)
                return await ReviewAsync(state.Target, userId,
                    "1С не сохранила целевые строки РО после PATCH.", ct);
            var confirmed = await ChangeAsync(state.Target, userId,
                Change.Patch, null, ct);
            if (!confirmed.IsSuccess) return confirmed.Error!;
            state = await store.ReadAsync(pickingAssemblyId, shippingOrderId, ct);
        }

        var posted = await writer.PostAsync(shippingOrderId, ct);
        if (!posted.IsSuccess)
            return await FailAsync(state.Target!, userId,
                posted.Error!.Message, ct);
        fresh = await reader.ReadAsync(shippingOrderId, ct);
        if (!fresh.IsSuccess)
            return await FailAsync(state.Target!, userId,
                fresh.Error!.Message, ct);
        current = fresh.Value!;
        if (current.DeletionMark is null || current.Posted is null)
            return await FailAsync(state.Target!, userId,
                "1С не вернула признак удаления или проведения после POST.", ct);
        if (current.DeletionMark == true)
            return await ReviewAsync(state.Target!, userId,
                "РО 1С помечен на удаление после POST.", ct);
        reached = writer.TargetMatches(state.Target!,
            state.PlanSourceSnapshotJson!, current);
        if (!reached.IsSuccess)
            return await FailAsync(state.Target!, userId,
                reached.Error!.Message, ct);
        if (reached.Value != true)
            return await ReviewAsync(state.Target!, userId,
                "РО 1С изменился после POST. Требуется решение Manager.", ct);
        if (current.Posted != true)
            return await FailAsync(state.Target!, userId,
                "1С не подтвердила проведение РО 1С.", ct);
        var finished = await ChangeAsync(state.Target!, userId,
            Change.Post, null, ct);
        return finished.IsSuccess ? Result(finished.Value!) : finished.Error!;
    }

    private async Task<OperationResult<ShippingExchangeResult>> FailAsync(
        ShippingExchangeTarget target, string userId, string error,
        CancellationToken ct)
    {
        var changed = await ChangeAsync(target, userId, Change.Failure,
            ShortError(error), ct);
        return changed.IsSuccess ? Result(changed.Value!) : changed.Error!;
    }

    private async Task<OperationResult<ShippingExchangeResult>> ReviewAsync(
        ShippingExchangeTarget target, string userId, string error,
        CancellationToken ct)
    {
        var changed = await ChangeAsync(target, userId, Change.Review,
            ShortError(error), ct);
        return changed.IsSuccess ? Result(changed.Value!) : changed.Error!;
    }

    private async Task<OperationResult<ShippingExchangeTarget>> ChangeAsync(
        ShippingExchangeTarget state, string userId, Change change,
        string? error, CancellationToken ct)
    {
        var requestId = Guid.NewGuid();
        var commandType = $"picking.exchange.{change.ToString().ToLowerInvariant()}";
        var hash = CommandExecutor.ComputeHash(
            $"{state.PickingAssemblyId:N}:{state.ShippingOrderId:N}:"
            + $"{state.OperationalRevision}:{error}");
        var result = await executor.ExecuteAsync(commandType, requestId, hash,
            userId, async (session, operationCt) =>
            {
                var target = await store.LoadTargetAsync(session,
                    state.PickingAssemblyId, state.ShippingOrderId, operationCt);
                if (target is null)
                    return OperationError.NotFound("Цель обмена РО 1С не найдена.");
                var now = clock.GetUtcNow();
                var transition = change switch
                {
                    Change.Patch => target.ConfirmPatch(state.OperationalRevision, now),
                    Change.Post => target.ConfirmPost(state.OperationalRevision, now),
                    Change.Failure => target.RecordFailure(state.OperationalRevision,
                        now, error!),
                    _ => target.RequireReview(state.OperationalRevision, now, error!)
                };
                if (!transition.IsSuccess) return transition.Error!;
                var audit = WarehouseAuditEvent.Create(commandType, now, userId,
                    null, null, null, null, null,
                    JsonSerializer.Serialize(new { state.Stage,
                        state.OperationalRevision }),
                    JsonSerializer.Serialize(new { target.Stage,
                        target.OperationalRevision,
                        Error = error is { Length: > 300 } ? error[..300] : error }),
                    commandUserId: userId, commandType: commandType,
                    requestId: requestId,
                    pickingAssemblyId: state.PickingAssemblyId);
                if (!audit.IsSuccess) return audit.Error!;
                return new CommandExecutionResult(state.PickingAssemblyId,
                    [audit.Value!]);
            }, ct);
        if (!result.IsSuccess) return result.Error!;
        var fresh = await store.ReadAsync(state.PickingAssemblyId,
            state.ShippingOrderId, ct);
        if (fresh.Target is null)
            return OperationError.NotFound("Цель обмена РО 1С не найдена.");
        return fresh.Target;
    }

    private static ShippingExchangeResult Result(ShippingExchangeTarget target) =>
        new(target.ShippingOrderId, target.Stage, target.LastError);

    private static string ShortError(string error) =>
        string.IsNullOrWhiteSpace(error) ? "Неизвестная ошибка обмена с 1С."
            : error.Trim().Length <= ShippingExchangeTarget.MaximumErrorLength
                ? error.Trim() : error.Trim()[..ShippingExchangeTarget.MaximumErrorLength];

    private enum Change { Patch, Post, Failure, Review }
}

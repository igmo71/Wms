using System.Text.Json;
using Wms.Application.Authorization;
using Wms.Application.CommandExecution;
using Wms.Common;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Application.Picking;

public sealed record StartPickingAssemblyRequest(Guid RequestId, Guid PickingAssemblyId, string UserId);

public sealed record PickingSourceStartResult(Guid ShippingOrderId, bool Confirmed, string? Error);

public sealed record StartPickingAssemblyResult(
    Guid PickingAssemblyId, PickingAssemblyStatus Status,
    IReadOnlyList<PickingSourceStartResult> Sources);

public sealed class StartPickingAssembly(
    CommandExecutor executor, ICommandExecutionStore commandStore,
    IPickingAssemblyStore store, SynchronizeShippingSource synchronize,
    IShippingSourceWriter writer, WarehousePermission permission, TimeProvider clock)
{
    private const string ClaimType = "picking.assembly.start.claim";
    private const string SourceType = "picking.assembly.start.source";
    private const string PatchIntentType = "picking.assembly.start.patch-intent";
    private const string ReleaseType = "picking.assembly.start.release";
    private const string ResponseType = "picking.assembly.start";

    public async Task<OperationResult<StartPickingAssemblyResult>> ExecuteAsync(
        StartPickingAssemblyRequest request, CancellationToken ct = default)
    {
        if (request is null || request.RequestId == Guid.Empty
            || request.PickingAssemblyId == Guid.Empty
            || string.IsNullOrWhiteSpace(request.UserId) || request.UserId.Length > 36)
            return OperationError.Invalid("Укажите сборку, идентификатор запроса и исполнителя.");
        var access = await permission.RequireOperatorAsync(request.UserId, ct);
        if (!access.IsSuccess)
            return access.Error!;
        var hash = CommandExecutor.ComputeHash(request.PickingAssemblyId.ToString("N"));
        var completed = await commandStore.FindReceiptAsync(
            request.UserId, ResponseType, request.RequestId, ct);
        if (completed is not null)
            return completed.RequestHash == hash
                ? await ReadResultAsync(request.PickingAssemblyId, ct)
                : OperationError.Conflict("Этот идентификатор запроса уже использован с другими данными.");

        var assembly = await store.ReadAssemblyAsync(request.PickingAssemblyId, ct);
        if (assembly is null)
            return OperationError.NotFound("Сборка WMS не найдена.");
        if (assembly.ResponsibleUserId != request.UserId.Trim())
            return OperationError.Conflict("Сначала примите сборку на себя.");
        if (assembly.Status == PickingAssemblyStatus.InProgress)
            return await ReadResultAsync(assembly.Id, ct);
        if (assembly.Status is not (PickingAssemblyStatus.Draft or PickingAssemblyStatus.Preparing))
            return OperationError.Conflict("Начало Отбора для этой сборки недоступно.");

        if (assembly.Status == PickingAssemblyStatus.Draft)
        {
            foreach (var link in assembly.ActiveSources)
            {
                var refreshed = await synchronize.ExecuteAsync(link.ShippingOrderId, ct);
                if (!refreshed.IsSuccess)
                    return refreshed.Error!;
            }
            assembly = await store.ReadAssemblyAsync(request.PickingAssemblyId, ct)
                ?? throw new InvalidOperationException("Сборка исчезла во время сверки.");
            foreach (var link in assembly.ActiveSources)
            {
                var source = await store.ReadSourceAsync(link.ShippingOrderId, ct);
                if (source is null)
                    return OperationError.NotFound("РО 1С не найден после сверки.");
                var eligible = PickingSourceEligibility.Plan(source, assembly.WarehouseId);
                if (!eligible.IsSuccess)
                    return eligible.Error!;
                var matching = ValidateSource(assembly, link.ShippingOrderId, source);
                if (!matching.IsSuccess)
                    return matching.Error!;
            }
        }

        var leaseId = Guid.NewGuid();
        var claimRequestId = Guid.NewGuid();
        var claimed = await executor.ExecuteAsync(ClaimType, claimRequestId, hash,
            request.UserId, async (session, operationCt) =>
            {
                var current = await store.LoadAssemblyAsync(session,
                    request.PickingAssemblyId, operationCt);
                if (current is null)
                    return OperationError.NotFound("Сборка WMS не найдена.");
                if (current.ResponsibleUserId != request.UserId.Trim())
                    return OperationError.Conflict("Сначала примите сборку на себя.");
                if (current.Status == PickingAssemblyStatus.Draft)
                {
                    if (current.ActiveSources.Count == 0)
                        return OperationError.Conflict("В сборке нет документов 1С.");
                    var frozen = current.FreezeForStart(request.UserId, clock.GetUtcNow());
                    if (!frozen.IsSuccess)
                        return frozen.Error!;
                }
                var now = clock.GetUtcNow();
                var claim = current.ClaimStart(leaseId, now, now.AddMinutes(10));
                if (!claim.IsSuccess)
                    return claim.Error!;
                return Effect("picking.assembly.start-claimed", current.Id,
                    request.UserId, ClaimType, claimRequestId, now,
                    new { request.RequestId, LeaseId = leaseId });
            }, ct);
        if (!claimed.IsSuccess)
            return claimed.Error!;

        try
        {
            assembly = await store.ReadAssemblyAsync(request.PickingAssemblyId, ct)
                ?? throw new InvalidOperationException("Сборка исчезла после фиксации начала.");
            foreach (var link in assembly.ActiveSources.Where(x => x.StartConfirmedAtUtc is null))
            {
                var error = await StartSourceAsync(request, leaseId, assembly,
                    link.ShippingOrderId, ct);
                await RecordSourceAsync(request, leaseId, link.ShippingOrderId, error, ct);
            }
        }
        finally
        {
            await ReleaseAsync(request, leaseId, CancellationToken.None);
        }

        var response = await executor.ExecuteAsync(ResponseType, request.RequestId, hash,
            request.UserId, (session, operationCt) => Task.FromResult(
                Effect("picking.assembly.start-response", request.PickingAssemblyId,
                    request.UserId, ResponseType, request.RequestId,
                    clock.GetUtcNow(), new { request.PickingAssemblyId })), ct);
        if (!response.IsSuccess)
            return response.Error!;
        return await ReadResultAsync(request.PickingAssemblyId, ct);
    }

    private async Task<string?> StartSourceAsync(StartPickingAssemblyRequest request,
        Guid leaseId, PickingAssembly assembly, Guid sourceId, CancellationToken ct)
    {
        var refreshed = await synchronize.ExecuteAsync(sourceId, ct);
        if (!refreshed.IsSuccess)
            return refreshed.Error!.Message;
        var source = await store.ReadSourceAsync(sourceId, ct);
        var validation = ValidateSource(assembly, sourceId, source);
        if (!validation.IsSuccess)
            return validation.Error!.Message;

        if (source!.Status == ShippingOrderStatus.Prepared)
        {
            await RecordPatchIntentAsync(request, leaseId, sourceId, ct);
            var patched = await writer.SetReadyForPickingAsync(sourceId, ct);
            if (!patched.IsSuccess)
                return patched.Error!.Message;
        }
        else if (assembly.ActiveSources.Single(x => x.ShippingOrderId == sourceId)
            .StartPatchRequestedAtUtc is null)
        {
            return "Статус РО 1С переключён вне WMS; требуется разбор перед началом Отбора.";
        }
        if (source.Status == ShippingOrderStatus.Prepared || !source.Posted)
        {
            var posted = await writer.PostAsync(sourceId, ct);
            if (!posted.IsSuccess)
                return posted.Error!.Message;
        }

        refreshed = await synchronize.ExecuteAsync(sourceId, ct);
        if (!refreshed.IsSuccess)
            return refreshed.Error!.Message;
        source = await store.ReadSourceAsync(sourceId, ct);
        validation = ValidateSource(assembly, sourceId, source);
        if (!validation.IsSuccess)
            return validation.Error!.Message;
        return source!.Status == ShippingOrderStatus.ReadyForPicking && source.Posted
            ? null
            : "1С не подтвердила проведение документа в статусе КОтбору.";
    }

    private async Task RecordPatchIntentAsync(StartPickingAssemblyRequest request,
        Guid leaseId, Guid sourceId, CancellationToken ct)
    {
        var intentId = Guid.NewGuid();
        var result = await executor.ExecuteAsync(PatchIntentType, intentId,
            CommandExecutor.ComputeHash($"{request.PickingAssemblyId:N}:{sourceId:N}:{leaseId:N}"),
            request.UserId, async (session, operationCt) =>
            {
                var assembly = await store.LoadAssemblyAsync(session,
                    request.PickingAssemblyId, operationCt);
                if (assembly is null)
                    return OperationError.NotFound("Сборка WMS не найдена.");
                var now = clock.GetUtcNow();
                var marked = assembly.RecordStartPatchIntent(leaseId, sourceId, now);
                if (!marked.IsSuccess)
                    return marked.Error!;
                return Effect("picking.assembly.start-patch-requested", assembly.Id,
                    request.UserId, PatchIntentType, intentId, now,
                    new { ShippingOrderId = sourceId });
            }, ct);
        if (!result.IsSuccess)
            throw new InvalidOperationException(result.Error!.Message);
    }

    private static OperationResult ValidateSource(PickingAssembly assembly,
        Guid sourceId, ShippingOrder? source)
    {
        if (source is null || source.LastSourceCheckError is not null)
            return OperationError.Conflict("Документ 1С не прошёл успешную сверку.");
        if (source.DeletionMark || source.WarehouseId != assembly.WarehouseId)
            return OperationError.Conflict("Склад документа 1С изменён или документ помечен на удаление.");
        if (source.Status is not (ShippingOrderStatus.Prepared or ShippingOrderStatus.ReadyForPicking))
            return OperationError.Conflict("Документ 1С не находится в статусе Подготовлен или КОтбору.");
        if (source.Status == ShippingOrderStatus.Prepared && !source.Posted)
            return OperationError.Conflict("Документ 1С снят с проведения.");

        var planned = assembly.PlanLines.Where(x => x.ShippingOrderId == sourceId)
            .ToDictionary(x => x.StockKeepingUnitId, x => x.Quantity);
        var current = source.Items.GroupBy(x => x.StockKeepingUnitId)
            .ToDictionary(x => x.Key, x => x.Sum(line => line.PlanQuantity));
        if (planned.Count != current.Count
            || planned.Any(x => !current.TryGetValue(x.Key, out var quantity)
                || quantity != x.Value))
            return OperationError.Conflict("Строки документа 1С изменились после фиксации плана сборки.");
        return OperationResult.Success();
    }

    private async Task RecordSourceAsync(StartPickingAssemblyRequest request,
        Guid leaseId, Guid sourceId, string? error, CancellationToken ct)
    {
        var progressId = Guid.NewGuid();
        var result = await executor.ExecuteAsync(SourceType, progressId,
            CommandExecutor.ComputeHash($"{request.PickingAssemblyId:N}:{sourceId:N}:{leaseId:N}"),
            request.UserId, async (session, operationCt) =>
            {
                var assembly = await store.LoadAssemblyAsync(session,
                    request.PickingAssemblyId, operationCt);
                if (assembly is null)
                    return OperationError.NotFound("Сборка WMS не найдена.");
                var now = clock.GetUtcNow();
                var recorded = assembly.RecordSourceStartResult(leaseId, sourceId, now,
                    error is { Length: > 2000 } ? error[..2000] : error);
                if (!recorded.IsSuccess)
                    return recorded.Error!;
                return Effect("picking.assembly.source-start-result", assembly.Id,
                    request.UserId, SourceType, progressId, now,
                    new { ShippingOrderId = sourceId,
                        Error = error is { Length: > 2000 } ? error[..2000] : error });
            }, ct);
        if (!result.IsSuccess)
            throw new InvalidOperationException(result.Error!.Message);
    }

    private async Task ReleaseAsync(StartPickingAssemblyRequest request,
        Guid leaseId, CancellationToken ct)
    {
        var releaseId = Guid.NewGuid();
        var result = await executor.ExecuteAsync(ReleaseType, releaseId,
            CommandExecutor.ComputeHash($"{request.PickingAssemblyId:N}:{leaseId:N}"),
            request.UserId, async (session, operationCt) =>
            {
                var assembly = await store.LoadAssemblyAsync(session,
                    request.PickingAssemblyId, operationCt);
                if (assembly is null)
                    return OperationError.NotFound("Сборка WMS не найдена.");
                var now = clock.GetUtcNow();
                if (assembly.ActiveSources.All(x => x.StartConfirmedAtUtc is not null))
                {
                    var completed = assembly.CompleteStart(leaseId, now);
                    if (!completed.IsSuccess)
                        return completed.Error!;
                }
                var released = assembly.ReleaseStart(leaseId);
                if (!released.IsSuccess)
                    return released.Error!;
                return Effect("picking.assembly.start-finished", assembly.Id,
                    request.UserId, ReleaseType, releaseId, now,
                    new { Status = assembly.Status.ToString() });
            }, ct);
        if (!result.IsSuccess)
            throw new InvalidOperationException(result.Error!.Message);
    }

    private async Task<OperationResult<StartPickingAssemblyResult>> ReadResultAsync(
        Guid assemblyId, CancellationToken ct)
    {
        var assembly = await store.ReadAssemblyAsync(assemblyId, ct);
        if (assembly is null)
            return OperationError.NotFound("Сборка WMS не найдена.");
        return new StartPickingAssemblyResult(assemblyId, assembly.Status,
            assembly.ActiveSources.Select(x => new PickingSourceStartResult(
                x.ShippingOrderId, x.StartConfirmedAtUtc is not null, x.LastStartError))
                .ToArray());
    }

    private static OperationResult<CommandExecutionResult> Effect(
        string eventType, Guid assemblyId, string userId, string commandType,
        Guid commandId, DateTimeOffset now, object data)
    {
        var audit = WarehouseAuditEvent.Create(eventType, now, userId, null,
            null, null, null, null, null, JsonSerializer.Serialize(data),
            commandUserId: userId, commandType: commandType,
            requestId: commandId, pickingAssemblyId: assemblyId);
        return audit.IsSuccess
            ? new CommandExecutionResult(assemblyId, [audit.Value!])
            : audit.Error!;
    }
}

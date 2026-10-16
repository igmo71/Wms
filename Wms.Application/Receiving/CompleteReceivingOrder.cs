using System.Text.Json;
using Wms.Application.Authorization;
using Wms.Application.CommandExecution;
using Wms.Common;
using Wms.Domain;

namespace Wms.Application.Receiving;

public sealed record CompleteReceivingOrderRequest(
    Guid RequestId, Guid OrderId, string UserId);

public sealed class CompleteReceivingOrder(
    CommandExecutor executor, IReceivingWorkStore store,
    SynchronizeReceivingSource synchronize, WarehousePermission permission,
    TimeProvider clock)
{
    private const string CommandType = "receiving.order.complete-cycle";

    public Task<OperationResult<Guid>> ExecuteAsync(
        CompleteReceivingOrderRequest request, CancellationToken ct = default)
    {
        if (request is null || request.OrderId == Guid.Empty)
            return Task.FromResult<OperationResult<Guid>>(
                OperationError.Invalid("Приходный ордер обязателен."));

        var hash = CommandExecutor.ComputeHash(request.OrderId.ToString("N"));
        return executor.ExecuteAsync(CommandType, request.RequestId, hash, request.UserId,
            async (session, operationCt) =>
            {
                var access = await permission.RequireReceivingWorkerAsync(
                    request.UserId, operationCt);
                if (!access.IsSuccess)
                    return access.Error!;
                var canManage = (await permission.RequireManagerAsync(
                    request.UserId, operationCt)).IsSuccess;

                var refKeys = await store.LoadSourceRefKeysAsync(request.OrderId, operationCt);
                var failedChecks = new List<string>();
                foreach (var refKey in refKeys)
                {
                    var refreshed = await synchronize.ExecuteAsync(refKey, operationCt);
                    if (!refreshed.IsSuccess)
                        failedChecks.Add($"{refKey}: {refreshed.Error!.Message}");
                }

                var order = await store.LoadOrderAsync(session, request.OrderId, operationCt);
                if (order is null)
                    return OperationError.NotFound("Приходный ордер не найден.");
                var sources = await store.LoadClosingSourcesAsync(session, order.Id, operationCt);
                if (sources.Count != refKeys.Count
                    || sources.Any(x => !refKeys.Contains(x.Source.RefKey)))
                    return OperationError.Conflict("Состав источников изменился. Повторите завершение.");
                if (sources.Any(x => x.Version is null))
                    return OperationError.Conflict("Версия связанного источника не найдена.");

                var planJson = JsonSerializer.Serialize(order.Items
                    .OrderBy(x => x.StockKeepingUnitId)
                    .Select(x => new { SkuId = x.StockKeepingUnitId,
                        Quantity = x.CurrentPlanQuantity }));
                var factJson = JsonSerializer.Serialize(order.Items
                    .OrderBy(x => x.StockKeepingUnitId)
                    .Select(x => new { SkuId = x.StockKeepingUnitId,
                        Quantity = x.FactQuantity }));
                var sourceCheckFailed = failedChecks.Count > 0
                    || sources.Any(x => x.Source.LastAttemptError is not null);
                var versions = sources.Select(x =>
                    (x.Source.Id, x.Version!.Id)).ToArray();
                var now = clock.GetUtcNow();
                var completed = order.CompleteCurrentCycle(
                    request.UserId, now, canManage, planJson, factJson,
                    sourceCheckFailed, versions);
                if (!completed.IsSuccess)
                    return completed.Error!;

                foreach (var version in completed.Value!.SourceVersions)
                    session.Stage(version);
                store.GuardClosingSources(session, sources);
                var checkError = string.Join("; ", failedChecks);
                if (checkError.Length > 500)
                    checkError = checkError[..500];
                var audit = ReceivingWorkAudit.Create(
                    "receiving.order.cycle-completed", now, request.UserId,
                    order.Id, null, null,
                    new { Status = "InReceiving", CycleNumber = completed.Value.Number },
                    new
                    {
                        Status = "Received", CycleId = completed.Value.Id,
                        SourceCheckFailed = sourceCheckFailed,
                        SourceCheckError = failedChecks.Count == 0 ? null : checkError
                    }, CommandType, request.RequestId);
                if (!audit.IsSuccess)
                    return audit.Error!;
                return new CommandExecutionResult(order.Id, [audit.Value!]);
            }, ct);
    }
}

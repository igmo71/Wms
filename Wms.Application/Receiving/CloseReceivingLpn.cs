using System.Globalization;
using System.Text.Json;
using Wms.Application.Authorization;
using Wms.Application.CommandExecution;
using Wms.Application.Inventory;
using Wms.Common;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Application.Receiving;

public sealed record CloseReceivingLpnCommand(
    Guid RequestId, Guid OrderId, Guid LpnId, string UserId,
    bool ConfirmExpiredDate = false, string? ManagerReason = null,
    long? ExpectedLpnRevision = null, bool FromWeb = false);

public sealed record CloseReceivingLpnResult(
    Guid LpnId, decimal? UsedPlan, decimal TotalFact, bool SourceCheckSucceeded,
    string? SourceCheckError, DateTimeOffset? LastSuccessfulCheckAtUtc,
    bool ManagerException, bool ExpiredDateConfirmed);

public sealed class CloseReceivingLpn(
    CommandExecutor executor, IReceivingWorkStore store,
    SynchronizeReceivingSource synchronize, IReceivingSourceParser parser,
    PostInventoryMovement.Handler posting, WarehousePermission permission,
    TimeProvider clock)
{
    private const string CommandType = "receiving.lpn.close";

    public async Task<OperationResult<CloseReceivingLpnResult>> ExecuteAsync(
        CloseReceivingLpnCommand command, CancellationToken ct = default)
    {
        if (command is null || command.OrderId == Guid.Empty || command.LpnId == Guid.Empty)
            return OperationError.Invalid("Ордер и LPN обязательны.");
        if (command.FromWeb && command.ExpectedLpnRevision is null)
            return OperationError.Invalid("Для закрытия в Web нужна текущая версия LPN.");
        if (command.ManagerReason is { Length: > 500 })
            return OperationError.Invalid("Причина менеджера слишком длинная.");

        // Keep the hash of existing Mobile requests unchanged for pending retries.
        var hash = CommandExecutor.ComputeHash(command.ExpectedLpnRevision is long revision
            ? JsonSerializer.Serialize(new
            {
                command.OrderId, command.LpnId, command.ConfirmExpiredDate,
                command.ManagerReason, ExpectedLpnRevision = revision
            })
            : JsonSerializer.Serialize(new
            {
                command.OrderId, command.LpnId, command.ConfirmExpiredDate,
                command.ManagerReason
            }));
        var executed = await executor.ExecuteAsync(CommandType, command.RequestId, hash, command.UserId,
            async (session, operationCt) =>
            {
                var access = command.FromWeb
                    ? await permission.RequireManagerAsync(command.UserId, operationCt)
                    : await permission.RequireOperatorAsync(command.UserId, operationCt);
                if (!access.IsSuccess)
                    return access.Error!;
                var canManage = (await permission.RequireManagerAsync(
                    command.UserId, operationCt)).IsSuccess;

                var refKeys = await store.LoadSourceRefKeysAsync(command.OrderId, operationCt);
                string? synchronizationError = null;
                foreach (var refKey in refKeys)
                {
                    var refreshed = await synchronize.ExecuteAsync(refKey, operationCt);
                    if (!refreshed.IsSuccess)
                        synchronizationError ??= $"Источник {refKey}: {refreshed.Error!.Message}";
                }

                var order = await store.LoadOrderAsync(session, command.OrderId, operationCt);
                if (order is null)
                    return OperationError.NotFound("Приходный ордер не найден.");
                var lpn = await store.LoadLpnAsync(session, command.LpnId, operationCt);
                if (lpn is null || lpn.ReceivingOrderId != order.Id)
                    return OperationError.NotFound("LPN не найден в ордере.");
                if (command.ExpectedLpnRevision is long expectedRevision
                    && lpn.OperationalRevision != expectedRevision)
                    return OperationError.Conflict("LPN изменился. Обновите его перед закрытием.");
                var item = lpn.Items.SingleOrDefault(x => !x.IsDeleted);
                if (item is null)
                    return OperationError.Invalid("Закрыть можно только LPN с положительным количеством одного SKU.");
                var orderItem = order.Items.SingleOrDefault(x =>
                    x.StockKeepingUnitId == item.StockKeepingUnitId);
                if (orderItem is null)
                    return OperationError.NotFound("SKU отсутствует в составе ордера.");
                var sku = await store.LoadSkuAsync(session, item.StockKeepingUnitId, operationCt);
                if (sku is null)
                    return OperationError.NotFound("SKU не найден.");

                var sources = await store.LoadClosingSourcesAsync(session, order.Id, operationCt);
                if (sources.Count != refKeys.Count
                    || sources.Any(x => !refKeys.Contains(x.Source.RefKey)))
                    return OperationError.Conflict("Состав источников изменился. Повторите закрытие.");
                var sourceCheckSucceeded = synchronizationError is null
                    && sources.All(x => x.Version is not null
                        && x.Source.LastAttemptError is null);
                var usedPlan = sourceCheckSucceeded
                    ? CalculatePlan(sources, order.WarehouseId, item.StockKeepingUnitId)
                    : orderItem.CurrentPlanQuantity;
                if (sourceCheckSucceeded)
                {
                    var changedPlan = orderItem.SetCurrentPlan(usedPlan);
                    if (!changedPlan.IsSuccess)
                        return changedPlan.Error!;
                }

                var now = clock.GetUtcNow();
                if (sku.TracksExpirationDate && item.ExpirationDate is null)
                    return OperationError.Conflict(
                        "Учёт срока годности SKU включён. Обновите LPN и укажите дату перед закрытием.");
                var expired = item.ExpirationDate is DateOnly date
                    && date < DateOnly.FromDateTime(now.UtcDateTime);
                if (expired && !command.ConfirmExpiredDate)
                    return OperationError.Conflict(
                        "У LPN прошла дата срока годности. Подтвердите закрытие явно.");

                var closed = order.CloseReceivingLpn(
                    lpn, command.UserId, now, canManage);
                if (!closed.IsSuccess)
                    return closed.Error!;
                var totalFact = closed.Value;
                var managerException = usedPlan is not decimal plan || totalFact > plan;
                if (managerException && !canManage)
                {
                    var factText = totalFact.ToString("0.000", CultureInfo.GetCultureInfo("ru-RU"));
                    return OperationError.Conflict(usedPlan is null
                        ? $"Суммарный факт {factText}, план неизвестен. Требуется решение Менеджера."
                        : $"Суммарный факт {factText} превышает план. Требуется решение Менеджера.");
                }

                store.GuardClosingInputs(session, orderItem, sku);
                store.GuardClosingSources(session, sources);
                var movement = InventoryMovement.Create(Guid.NewGuid(), order.WarehouseId,
                    null, lpn.OriginalReceivingLocationId, item.StockKeepingUnitId,
                    item.Quantity, now, RecorderType.ReceivingOrder, order.Id,
                    1, command.UserId,
                    item.ExpirationDate, lpn.Id);
                if (!movement.IsSuccess)
                    return movement.Error!;
                var posted = await posting.HandleAsync(new PostInventoryMovement.Command(
                    movement.Value!, InventoryPostingKind.Receipt, now), session, operationCt);
                if (!posted.IsSuccess)
                    return posted.Error!;

                var versionIds = sources.Select(x => x.Version?.Id)
                    .OfType<Guid>().Distinct().ToArray();
                var audit = WarehouseAuditEvent.Create("receiving.lpn.closed", now,
                    command.UserId, null, order.Id, lpn.Id, item.StockKeepingUnitId, null,
                    JsonSerializer.Serialize(new { Status = "Open", item.Quantity,
                        item.ExpirationDate }),
                    JsonSerializer.Serialize(new
                    {
                        Status = "Closed", LpnId = lpn.Id,
                        UsedPlan = usedPlan, TotalFact = totalFact,
                        SourceCheckSucceeded = sourceCheckSucceeded,
                        SourceCheckError = synchronizationError is null ? null
                            : synchronizationError[..Math.Min(500, synchronizationError.Length)],
                        LastSuccessfulCheckAtUtc = sources.Select(x =>
                            x.Source.LastSuccessfulCheckAtUtc).Min(),
                        ManagerException = managerException,
                        command.ManagerReason, ExpiredDateConfirmed = expired,
                        ClosedBy = command.UserId
                    }),
                    [movement.Value!.Id], versionIds, command.UserId,
                    CommandType, command.RequestId);
                if (!audit.IsSuccess)
                    return audit.Error!;
                return new CommandExecutionResult(lpn.Id, [audit.Value!]);
            }, ct);
        if (!executed.IsSuccess)
            return executed.Error!;
        var result = await store.LoadClosingResultAsync(command.UserId, command.RequestId, ct);
        return result ?? throw new InvalidOperationException(
            "Успешное закрытие LPN не содержит сохранённого результата аудита.");
    }

    private decimal? CalculatePlan(
        IReadOnlyList<ReceivingSourceState> sources, Guid warehouseId, Guid skuId)
    {
        var contributions = new List<(ParsedReceivingSource Source, ReceivingSourcePlan Plan)>(
            sources.Count);
        foreach (var state in sources)
        {
            var version = state.Version;
            if (version is null)
                return null;
            var parsed = parser.Parse(state.Source.RefKey, version.SnapshotJson);
            if (!parsed.IsSuccess)
                return null;
            contributions.Add((parsed.Value!, version.CalculatePlan()));
        }
        return ReceivingOrderPlanProjection.Sum(warehouseId, contributions)
            .GetValueOrDefault(skuId, 0m);
    }
}

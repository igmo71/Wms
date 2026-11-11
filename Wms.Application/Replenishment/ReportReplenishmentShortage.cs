using System.Globalization;
using System.Text.Json;
using Wms.Application.Authorization;
using Wms.Application.CommandExecution;
using Wms.Application.Inventory;
using Wms.Common;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Application.Replenishment;

public sealed record ReportReplenishmentShortageRequest(
    Guid RequestId, Guid ReplenishmentTaskId, Guid SourceCycleId,
    Guid StockKeepingUnitId, DateOnly? ExpirationDate,
    decimal ExpectedQuantity, decimal FoundQuantity, string UserId,
    string? Comment = null);

public sealed class ReportReplenishmentShortage(
    CommandExecutor executor, IReplenishmentStore replenishments,
    IStockAvailabilityStore stock, IStockShortageStore shortages,
    WarehousePermission permission, TimeProvider clock)
{
    private const string CommandType = "replenishment.shortage.report";

    public Task<OperationResult<Guid>> ExecuteAsync(
        ReportReplenishmentShortageRequest request, CancellationToken ct = default)
    {
        if (request is null || request.RequestId == Guid.Empty
            || request.ReplenishmentTaskId == Guid.Empty || request.SourceCycleId == Guid.Empty
            || request.StockKeepingUnitId == Guid.Empty
            || !WarehouseQuantity.IsPositive(request.ExpectedQuantity)
            || !WarehouseQuantity.IsNonNegative(request.FoundQuantity)
            || request.FoundQuantity >= request.ExpectedQuantity
            || request.Comment?.Trim().Length > StockShortage.MaximumCommentLength
            || string.IsNullOrWhiteSpace(request.UserId) || request.UserId.Length > 36)
            return Task.FromResult<OperationResult<Guid>>(
                OperationError.Invalid("Укажите задание, источник, SKU и меньшее найденное количество."));
        var hash = CommandExecutor.ComputeHash(string.Join(":", new[]
        {
            request.ReplenishmentTaskId.ToString("N"),
            request.SourceCycleId.ToString("N"),
            request.StockKeepingUnitId.ToString("N"),
            request.ExpirationDate?.ToString("yyyy-MM-dd") ?? "",
            request.ExpectedQuantity.ToString(CultureInfo.InvariantCulture),
            request.FoundQuantity.ToString(CultureInfo.InvariantCulture),
            request.Comment?.Trim() ?? ""
        }));
        return executor.ExecuteAsync(CommandType, request.RequestId, hash,
            request.UserId, async (session, token) =>
            {
                var access = await permission.RequireOperatorAsync(request.UserId, token);
                if (!access.IsSuccess) return access.Error!;
                var state = await replenishments.LoadActiveSourceAsync(session,
                    request.ReplenishmentTaskId, token);
                var task = state.Task;
                if (task is null)
                    return OperationError.NotFound("Задание Подпитки не найдено.");
                if (task.Status != ReplenishmentTaskStatus.InProgress
                    || task.AssignedTo != request.UserId.Trim()
                    || task.StockKeepingUnitId != request.StockKeepingUnitId
                    || request.ExpectedQuantity > task.RemainingQuantity
                    || !task.SourceCycles.Any(x => x.Id == request.SourceCycleId
                        && x.CompletedAtUtc is null))
                    return OperationError.Conflict(
                        "Снятый поддон, исполнитель или невыполненное количество изменились.");
                var source = state.Source;
                if (source is null || source.WarehouseId != task.WarehouseId
                    || source.IsFolder || source.DeletionMark
                    || source.Warehouse?.DeletionMark != false
                    || source.Zone is null || source.Zone.DeletionMark
                    || source.Zone.WarehouseId != task.WarehouseId
                    || source.Zone.Type != ZoneType.Reserve
                    || source.ActiveLock?.OwnerType != StorageLocationLockOwnerType.ReplenishmentTask
                    || source.ActiveLock.OwnerId != task.Id)
                    return OperationError.Conflict("Исходная ячейка больше не удерживается заданием.");
                var available = await stock.ReadSkuAsync(session, task.WarehouseId,
                    task.StockKeepingUnitId, token);
                var group = available.SingleOrDefault(x =>
                    x.Group.StorageLocationId == source.Id
                    && x.Group.ExpirationDate == request.ExpirationDate);
                if (group is null || request.ExpectedQuantity > group.AvailableForReplenishment(task.Id))
                    return OperationError.Conflict(
                        "Ожидаемое количество превышает свободный остаток группы срока.");

                var now = clock.GetUtcNow();
                var shortage = StockShortage.Report(Guid.NewGuid(), task.WarehouseId,
                    source.Id, task.StockKeepingUnitId, request.ExpirationDate,
                    StockShortageOrigin.Replenishment, task.Id,
                    request.ExpectedQuantity, request.FoundQuantity,
                    request.Comment, request.UserId, now);
                if (!shortage.IsSuccess) return shortage.Error!;
                var blocked = await shortages.ReportAsync(session, shortage.Value!, token);
                if (!blocked.IsSuccess) return blocked.Error!;
                var audit = WarehouseAuditEvent.Create(
                    "replenishment.shortage.reported", now, request.UserId,
                    null, null, null, task.StockKeepingUnitId, null, null,
                    JsonSerializer.Serialize(new { TaskId = task.Id,
                        CycleId = request.SourceCycleId, ShortageId = shortage.Value!.Id,
                        SourceLocationId = source.Id, request.ExpirationDate,
                        request.ExpectedQuantity, request.FoundQuantity,
                        Comment = request.Comment?.Trim() }),
                    commandUserId: request.UserId, commandType: CommandType,
                    requestId: request.RequestId,
                    pickingAssemblyId: task.PickingAssemblyId);
                if (!audit.IsSuccess) return audit.Error!;
                return new CommandExecutionResult(shortage.Value!.Id, [audit.Value!]);
            }, ct);
    }
}

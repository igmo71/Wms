using System.Text.Json;
using Wms.Application.CommandExecution;
using Wms.Application.Replenishment;
using Wms.Common;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Application.Picking;

internal static class CancelUnneededReplenishments
{
    public static async Task<OperationResult<IReadOnlyList<WarehouseAuditEvent>>> ExecuteAsync(
        IReplenishmentStore replenishments, ICommandExecutionContext session,
        PickingAssembly assembly, IReadOnlyCollection<PickingEntry> entries,
        IReadOnlyCollection<Guid> affectedSkuIds, string userId,
        DateTimeOffset now, string commandType, Guid requestId,
        CancellationToken ct)
    {
        var affected = affectedSkuIds.ToHashSet();
        if (affected.Count == 0)
            return OperationResult<IReadOnlyList<WarehouseAuditEvent>>.Success([]);

        var planned = assembly.PlanLines.Where(x => affected.Contains(x.StockKeepingUnitId))
            .GroupBy(x => x.StockKeepingUnitId)
            .ToDictionary(x => x.Key, x => x.Sum(line => line.Quantity));
        var picked = entries.Where(x => affected.Contains(x.StockKeepingUnitId))
            .GroupBy(x => x.StockKeepingUnitId)
            .ToDictionary(x => x.Key, x => x.Sum(entry => entry.Quantity));
        var unneeded = affected.Where(skuId =>
            picked.GetValueOrDefault(skuId) >= planned.GetValueOrDefault(skuId))
            .ToHashSet();
        if (unneeded.Count == 0)
            return OperationResult<IReadOnlyList<WarehouseAuditEvent>>.Success([]);

        var tasks = await replenishments.LoadForAssemblyAsync(session, assembly.Id, ct);
        var audits = new List<WarehouseAuditEvent>();
        foreach (var task in tasks.Where(x => x.Status == ReplenishmentTaskStatus.Ordered
            && unneeded.Contains(x.StockKeepingUnitId)))
        {
            var cancelled = task.Cancel(userId, now);
            if (!cancelled.IsSuccess) return cancelled.Error!;
            var audit = WarehouseAuditEvent.Create("replenishment.cancelled.unneeded",
                now, userId, null, null, null, task.StockKeepingUnitId,
                null, JsonSerializer.Serialize(new { TaskId = task.Id,
                    Status = ReplenishmentTaskStatus.Ordered }),
                JsonSerializer.Serialize(new { TaskId = task.Id, task.Status,
                    Reason = "Потребность по SKU закрыта." }),
                commandUserId: userId, commandType: commandType,
                requestId: requestId, pickingAssemblyId: assembly.Id);
            if (!audit.IsSuccess) return audit.Error!;
            audits.Add(audit.Value!);
        }
        return OperationResult<IReadOnlyList<WarehouseAuditEvent>>.Success(audits);
    }
}

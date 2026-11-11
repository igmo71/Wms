using Wms.Application.Authorization;
using Wms.Common;
using Wms.Domain.Enums;

namespace Wms.Application.Picking;

public sealed record PickingClosureSourceCheck(
    Guid ShippingOrderId, string? Number, string? CurrentSourceJson,
    IReadOnlyDictionary<Guid, decimal> PlanBySku,
    IReadOnlyDictionary<Guid, decimal>? CurrentBySku,
    Guid? PreviousReceiverId, Guid? CurrentReceiverId,
    bool ReceiverChanged, bool PlanMatchesSource, string? BlockingError);

public sealed record PickingClosureCheck(
    Guid PickingAssemblyId, long Revision,
    IReadOnlyList<PickingClosureSourceCheck> Sources,
    IReadOnlyList<string> HardBlocks,
    IReadOnlyList<string> ManagerIssues,
    PickingAllocationPreview? Allocation,
    bool FactMatchesPlan,
    bool CanOperatorClose,
    bool CanManagerClose);

public sealed class CheckPickingClosure(
    IPickingAssemblyStore assemblies,
    SynchronizeShippingSource synchronize,
    IShippingStoredSnapshotParser parser,
    AllocatePickingFact allocator,
    WarehousePermission permission,
    TimeProvider clock)
{
    public async Task<OperationResult<PickingClosureCheck>> ExecuteAsync(
        Guid assemblyId, string userId,
        IReadOnlyCollection<PickingAllocationInput>? managerAllocations = null,
        CancellationToken ct = default)
    {
        if (assemblyId == Guid.Empty || string.IsNullOrWhiteSpace(userId))
            return OperationError.Invalid("Укажите сборку и сотрудника.");
        var access = managerAllocations is { Count: > 0 }
            ? await permission.RequireManagerAsync(userId, ct)
            : await permission.RequireOperatorAsync(userId, ct);
        if (!access.IsSuccess && managerAllocations is not { Count: > 0 })
            access = await permission.RequireManagerAsync(userId, ct);
        if (!access.IsSuccess) return access.Error!;

        var assembly = await assemblies.ReadAssemblyAsync(assemblyId, ct);
        if (assembly is null)
            return OperationError.NotFound("Сборка WMS не найдена.");
        if (assembly.Status != PickingAssemblyStatus.InProgress)
            return OperationError.Conflict("Проверить закрытие можно только в начатой сборке.");

        var hard = new List<string>();
        var manager = new List<string>();
        var sourceChecks = new List<PickingClosureSourceCheck>();
        foreach (var link in assembly.ActiveSources.OrderBy(x => x.ShippingOrderId))
        {
            // Every linked document gets a fresh GET. A failed document does not
            // hide the state of the others from the warehouse manager.
            var refreshed = await synchronize.ExecuteAsync(link.ShippingOrderId, ct);
            var source = await assemblies.ReadSourceAsync(link.ShippingOrderId, ct);
            var original = parser.Parse(link.ShippingOrderId,
                link.PlanSourceSnapshotJson);
            var planned = assembly.PlanLines.Where(x =>
                x.ShippingOrderId == link.ShippingOrderId)
                .ToDictionary(x => x.StockKeepingUnitId, x => x.Quantity);
            IReadOnlyDictionary<Guid, decimal>? current = !refreshed.IsSuccess || source is null ? null
                : source.Items.GroupBy(x => x.StockKeepingUnitId)
                    .ToDictionary(x => x.Key, x => x.Sum(line => line.PlanQuantity));
            string? block = null;
            if (!refreshed.IsSuccess)
                block = refreshed.Error!.Message;
            else if (!original.IsSuccess || original.Value!.WarehouseId is null
                || original.Value.WarehouseOperation == WarehouseOperation.Unknown
                || original.Value.ReceiverId is null || original.Value.ReceiverType is null)
                block = "Исходный снимок РО 1С не позволяет проверить склад и получателя.";
            else if (source is null || source.CurrentSourceJson is null
                || source.LastSourceCheckError is not null)
                block = "Текущее состояние РО 1С не прочитано.";
            else if (source.DeletionMark || !source.Posted
                || source.Status != ShippingOrderStatus.ReadyForPicking
                || source.WarehouseId != assembly.WarehouseId
                || original.Value.WarehouseId != assembly.WarehouseId
                || source.WarehouseOperation != original.Value.WarehouseOperation)
                block = "РО 1С изменил склад, операцию, статус или проведение либо помечен на удаление.";
            else
            {
                var reconstructable = original.Value.Items.Where(x => x.IsRegular
                        && x.StockKeepingUnitId is not null)
                    .Select(x => x.StockKeepingUnitId!.Value)
                    .Concat(source.Items.Select(x => x.StockKeepingUnitId))
                    .ToHashSet();
                if (planned.Any(x => x.Value > 0 && !reconstructable.Contains(x.Key)))
                    block = "В утверждённом плане есть SKU без строки для восстановления документа 1С.";
            }
            if (block is not null)
                hard.Add($"РО 1С {source?.Number ?? link.ShippingOrderId.ToString()}: {block}");

            var receiverChanged = block is null
                && (source!.ReceiverId != original.Value!.ReceiverId
                    || source.ReceiverType != original.Value.ReceiverType);
            var planMatches = current is not null && planned.Count == current.Count
                && planned.All(x => current.TryGetValue(x.Key, out var value)
                    && value == x.Value);
            if (receiverChanged)
                manager.Add($"РО 1С {source!.Number ?? link.ShippingOrderId.ToString()}: изменён получатель.");
            if (block is null && !planMatches)
                manager.Add($"РО 1С {source!.Number ?? link.ShippingOrderId.ToString()}: план WMS отличается от текущих строк 1С.");
            sourceChecks.Add(new PickingClosureSourceCheck(
                link.ShippingOrderId, source?.Number,
                refreshed.IsSuccess ? source?.CurrentSourceJson : null,
                planned, current, original.IsSuccess ? original.Value!.ReceiverId : null,
                source?.ReceiverId, receiverChanged, planMatches, block));
        }
        if (sourceChecks.Count == 0)
            hard.Add("У сборки нет связанных РО 1С.");
        var currentWork = await assemblies.ReadWorkAsync(assemblyId, null, ct);
        if (currentWork.Assembly is null
            || currentWork.Assembly.Status != PickingAssemblyStatus.InProgress
            || currentWork.Assembly.OperationalRevision != assembly.OperationalRevision)
            hard.Add("Сборка изменилась во время проверки. Повторите сверку.");
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        if (currentWork.Entries.Any(x => x.Quantity > 0
            && x.ExpirationDate is DateOnly expiry && expiry < today))
            hard.Add("Факт содержит товар с истёкшим сроком годности.");
        var plannedTotal = assembly.PlanLines.GroupBy(x => x.StockKeepingUnitId)
            .ToDictionary(x => x.Key, x => x.Sum(line => line.Quantity));
        var pickedTotal = currentWork.Entries.Where(x => x.Quantity > 0)
            .GroupBy(x => x.StockKeepingUnitId)
            .ToDictionary(x => x.Key, x => x.Sum(line => line.Quantity));
        var factMatchesPlan = plannedTotal.Count == pickedTotal.Count
            ? plannedTotal.All(x => pickedTotal.TryGetValue(x.Key, out var picked)
                && picked == x.Value)
            : plannedTotal.Keys.Union(pickedTotal.Keys).All(x =>
                plannedTotal.GetValueOrDefault(x) == pickedTotal.GetValueOrDefault(x));
        if (!factMatchesPlan)
            manager.Add("Общий план WMS отличается от физического факта.");
        var proposed = allocator.Execute(assembly, currentWork.Entries,
            managerAllocations);
        PickingAllocationPreview? allocation = null;
        if (!proposed.IsSuccess)
            manager.Add(proposed.Error!.Message);
        else
        {
            allocation = proposed.Value!;
            if (allocation.RequiresManagerDecision.Count > 0)
                manager.Add("Недобор общего SKU требует распределения руководителем по документам 1С.");
        }
        var canManager = hard.Count == 0 && allocation is not null
            && allocation.RequiresManagerDecision.Count == 0;
        var canOperator = canManager && manager.Count == 0;
        return new PickingClosureCheck(assemblyId, assembly.OperationalRevision,
            sourceChecks, hard, manager, allocation, factMatchesPlan,
            canOperator, canManager);
    }
}

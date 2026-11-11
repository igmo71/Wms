using Wms.Application.Authorization;
using Wms.Common;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Application.Picking;

public sealed record PickingAllocationInput(Guid ShippingOrderId,
    Guid StockKeepingUnitId, decimal Quantity);

public sealed record PickingAllocationPreview(
    IReadOnlyList<PickingAllocation> Allocations,
    IReadOnlyList<Guid> RequiresManagerDecision);

public sealed class AllocatePickingFact
{
    public OperationResult<PickingAllocationPreview> Execute(
        PickingAssembly assembly, IReadOnlyCollection<PickingEntry> entries,
        IReadOnlyCollection<PickingAllocationInput>? managerAllocations = null)
    {
        if (assembly is null || entries is null
            || assembly.Status != PickingAssemblyStatus.InProgress)
            return OperationError.Conflict("Для распределения нужна начатая сборка.");
        if (entries.Any(x => x.PickingAssemblyId != assembly.Id
            || !WarehouseQuantity.IsNonNegative(x.Quantity)))
            return OperationError.Invalid("Факт относится к другой сборке или имеет неверное количество.");
        var manual = managerAllocations?.ToArray() ?? [];
        if (manual.Any(x => x.ShippingOrderId == Guid.Empty
            || x.StockKeepingUnitId == Guid.Empty
            || !WarehouseQuantity.IsNonNegative(x.Quantity))
            || manual.GroupBy(x => new { x.ShippingOrderId, x.StockKeepingUnitId })
                .Any(x => x.Count() > 1))
            return OperationError.Invalid("Ручное распределение содержит неверную или повторную строку.");
        var plans = assembly.PlanLines.ToDictionary(
            x => (x.ShippingOrderId, x.StockKeepingUnitId), x => x.Quantity);
        if (manual.Any(x => !plans.ContainsKey((x.ShippingOrderId, x.StockKeepingUnitId))))
            return OperationError.Conflict("Распределение содержит документ или SKU вне плана WMS.");
        var fact = entries.Where(x => x.Quantity > 0)
            .GroupBy(x => x.StockKeepingUnitId)
            .ToDictionary(x => x.Key, x => x.Sum(row => row.Quantity));
        if (fact.Keys.Any(skuId => !plans.Keys.Any(x => x.StockKeepingUnitId == skuId)))
            return OperationError.Conflict("Факт содержит SKU без утверждённого плана WMS.");
        var allocations = new List<PickingAllocation>();
        var unresolved = new List<Guid>();
        foreach (var skuId in plans.Keys.Select(x => x.StockKeepingUnitId).Distinct().Order())
        {
            var perSource = plans.Where(x => x.Key.StockKeepingUnitId == skuId).ToArray();
            var totalPlan = perSource.Sum(x => x.Value);
            var quantity = fact.GetValueOrDefault(skuId);
            if (quantity > totalPlan)
                return OperationError.Conflict("Факт SKU превышает суммарный план документов 1С.");
            var supplied = manual.Where(x => x.StockKeepingUnitId == skuId).ToArray();
            var positiveSources = perSource.Count(x => x.Value > 0);
            var needsDecision = quantity > 0 && quantity < totalPlan && positiveSources > 1;
            if (needsDecision && supplied.Length == 0)
            {
                unresolved.Add(skuId);
                continue;
            }
            if (supplied.Length > 0 && supplied.Length != perSource.Length)
                return OperationError.Invalid("Укажите распределение по каждому документу общего SKU, включая нули.");
            foreach (var source in perSource)
            {
                var assigned = supplied.Length > 0
                    ? supplied.Single(x => x.ShippingOrderId == source.Key.ShippingOrderId).Quantity
                    : quantity == totalPlan ? source.Value
                    : positiveSources == 1 && source.Value > 0 ? quantity : 0;
                if (assigned > source.Value)
                    return OperationError.Conflict("Распределение по РО 1С превышает его план WMS.");
                var allocation = PickingAllocation.Create(assembly.Id,
                    source.Key.ShippingOrderId, skuId, assigned);
                if (!allocation.IsSuccess) return allocation.Error!;
                allocations.Add(allocation.Value!);
            }
            if (allocations.Where(x => x.StockKeepingUnitId == skuId)
                    .Sum(x => x.Quantity) != quantity)
                return OperationError.Conflict("Распределение SKU не совпадает с фактом WMS.");
        }
        return new PickingAllocationPreview(allocations, unresolved);
    }
}

public sealed class PreviewPickingAllocation(IPickingAssemblyStore assemblies,
    AllocatePickingFact allocator, WarehousePermission permission)
{
    public async Task<OperationResult<PickingAllocationPreview>> ExecuteAsync(
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
        var state = await assemblies.ReadWorkAsync(assemblyId, null, ct);
        return state.Assembly is null
            ? OperationError.NotFound("Сборка WMS не найдена.")
            : allocator.Execute(state.Assembly, state.Entries, managerAllocations);
    }
}

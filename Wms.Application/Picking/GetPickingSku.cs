using Wms.Application.Authorization;
using Wms.Application.Inventory;
using Wms.Common;
using Wms.Domain.Enums;

namespace Wms.Application.Picking;

public sealed record PickingSkuView(Guid PickingAssemblyId, Guid StockKeepingUnitId,
    decimal PlannedQuantity, decimal ConfirmedQuantity, decimal RemainingQuantity,
    IReadOnlyList<StockAvailability> Availability);

public sealed class GetPickingSku(
    IPickingAssemblyStore assemblies, IStockAvailabilityStore stock,
    WarehousePermission permission)
{
    public async Task<OperationResult<PickingSkuView>> ExecuteAsync(
        Guid assemblyId, Guid skuId, string userId, CancellationToken ct = default)
    {
        if (assemblyId == Guid.Empty || skuId == Guid.Empty || string.IsNullOrWhiteSpace(userId))
            return OperationError.Invalid("Укажите сборку, SKU и сотрудника.");
        var access = await permission.RequireOperatorAsync(userId, ct);
        if (!access.IsSuccess) return access.Error!;
        var state = await assemblies.ReadWorkAsync(assemblyId, null, ct);
        var assembly = state.Assembly;
        if (assembly is null) return OperationError.NotFound("Сборка WMS не найдена.");
        if (assembly.Status != PickingAssemblyStatus.InProgress
            || assembly.ResponsibleUserId != userId.Trim())
            return OperationError.Conflict("Для Отбора примите начатую сборку на себя.");
        var planned = assembly.PlanLines.Where(x => x.StockKeepingUnitId == skuId)
            .Sum(x => x.Quantity);
        if (planned <= 0)
            return OperationError.Conflict("SKU не входит в текущий план сборки.");
        var confirmed = state.Entries.Where(x => x.StockKeepingUnitId == skuId)
            .Sum(x => x.Quantity);
        return new PickingSkuView(assemblyId, skuId, planned, confirmed,
            Math.Max(0, planned - confirmed),
            await stock.ReadSkuAsync(assembly.WarehouseId, skuId, ct));
    }
}

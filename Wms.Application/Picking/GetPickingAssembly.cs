using Wms.Application.Authorization;
using Wms.Common;

namespace Wms.Application.Picking;

public sealed class GetPickingAssembly(
    IPickingAssemblyQueryStore store, WarehousePermission permission)
{
    public async Task<OperationResult<PickingAssemblyView>> ExecuteAsync(
        Guid assemblyId, string userId, CancellationToken ct = default)
    {
        if (assemblyId == Guid.Empty || string.IsNullOrWhiteSpace(userId))
            return OperationError.Invalid("Укажите сборку и сотрудника.");
        var operatorAccess = await permission.RequireOperatorAsync(userId, ct);
        if (!operatorAccess.IsSuccess)
        {
            var managerAccess = await permission.RequireManagerAsync(userId, ct);
            if (!managerAccess.IsSuccess)
                return OperationError.Conflict("Для просмотра сборки нужна роль Operator или Manager.");
        }
        var assembly = await store.GetAsync(assemblyId, ct);
        return assembly is null
            ? OperationError.NotFound("Сборка WMS не найдена.")
            : assembly;
    }
}

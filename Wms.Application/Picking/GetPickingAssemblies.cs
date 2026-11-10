using Wms.Application.Authorization;
using Wms.Common;

namespace Wms.Application.Picking;

public sealed class GetPickingAssemblies(
    IPickingAssemblyQueryStore store, WarehousePermission permission)
{
    public async Task<OperationResult<IReadOnlyList<PickingAssemblyListItem>>> ExecuteAsync(
        Guid warehouseId, string userId, CancellationToken ct = default)
    {
        if (warehouseId == Guid.Empty || string.IsNullOrWhiteSpace(userId))
            return OperationError.Invalid("Укажите склад и сотрудника.");
        var access = await permission.RequireOperatorAsync(userId, ct);
        if (!access.IsSuccess)
            return access.Error!;
        var items = await store.ListForOperatorAsync(warehouseId, userId.Trim(), ct);
        return OperationResult<IReadOnlyList<PickingAssemblyListItem>>.Success(items);
    }
}

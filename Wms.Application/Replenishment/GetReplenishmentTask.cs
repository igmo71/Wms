using Wms.Application.Authorization;
using Wms.Common;

namespace Wms.Application.Replenishment;

public sealed class GetReplenishmentTask(
    IReplenishmentStore store, WarehousePermission permission)
{
    public async Task<OperationResult<ReplenishmentTaskDetail>> ExecuteAsync(
        Guid taskId, string userId, CancellationToken ct = default)
    {
        if (taskId == Guid.Empty || string.IsNullOrWhiteSpace(userId))
            return OperationError.Invalid("Укажите задание Подпитки и подпитчика.");
        var access = await permission.RequireOperatorAsync(userId, ct);
        if (!access.IsSuccess) return access.Error!;
        var detail = await store.ReadTaskAsync(taskId, ct);
        return detail is null
            ? OperationError.NotFound("Задание Подпитки не найдено.")
            : detail;
    }
}

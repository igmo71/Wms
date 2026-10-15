using Wms.Common;

namespace Wms.Application.Authorization;

public interface IWarehousePermissionStore
{
    Task<bool> HasManagerRoleAsync(string userId, CancellationToken ct);
    Task<bool> HasReceivingRoleAsync(string userId, CancellationToken ct);
}

public sealed class WarehousePermission(IWarehousePermissionStore store)
{
    public const string ManagerRole = "Manager";
    public const string OperatorRole = "Operator";

    public async Task<OperationResult> RequireManagerAsync(string? userId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId) || !await store.HasManagerRoleAsync(userId, ct))
            return OperationError.Conflict("Для этого действия требуется роль Manager.");

        return OperationResult.Success();
    }

    public async Task<OperationResult> RequireReceivingWorkerAsync(
        string? userId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId)
            || !await store.HasReceivingRoleAsync(userId, ct))
            return OperationError.Conflict("Для этого действия требуется роль Operator или Manager.");

        return OperationResult.Success();
    }
}

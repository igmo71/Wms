using Wms.Common;

namespace Wms.Application.Authorization;

public interface IWarehousePermissionStore
{
    Task<bool> HasManagerRoleAsync(string userId, CancellationToken ct);
    Task<bool> HasOperatorRoleAsync(string userId, CancellationToken ct);
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

    public async Task<OperationResult> RequireOperatorAsync(
        string? userId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId)
            || !await store.HasOperatorRoleAsync(userId, ct))
            return OperationError.Conflict("Для этого действия требуется роль Operator.");

        return OperationResult.Success();
    }
}

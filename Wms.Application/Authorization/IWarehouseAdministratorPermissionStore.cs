namespace Wms.Application.Authorization;

public interface IWarehouseAdministratorPermissionStore
{
    Task<bool> HasAdministratorRoleAsync(string userId, CancellationToken ct);
}

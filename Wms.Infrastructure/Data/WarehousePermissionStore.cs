using Microsoft.EntityFrameworkCore;
using Wms.Application.Authorization;

namespace Wms.Data;

internal sealed class WarehousePermissionStore(IDbContextFactory<ApplicationDbContext> contextFactory)
    : IWarehousePermissionStore
{
    public async Task<bool> HasManagerRoleAsync(string userId, CancellationToken ct)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);

        return await (
            from membership in context.UserRoles
            join role in context.Roles on membership.RoleId equals role.Id
            where membership.UserId == userId && role.Name == WarehousePermission.ManagerRole
            select membership).AnyAsync(ct);
    }
}

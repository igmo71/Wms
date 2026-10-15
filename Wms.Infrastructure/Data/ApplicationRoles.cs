using Wms.Application.Authorization;

namespace Wms.Data;

public static class ApplicationRoles
{
    public const string Administrator = nameof(Administrator);
    public const string Manager = WarehousePermission.ManagerRole;
    public const string Operator = nameof(Operator);
    public const string WmsUser = Administrator + "," + Manager + "," + Operator;
    public const string ManagerOrAdministrator = Manager + "," + Administrator;

    public static readonly string[] All = [Administrator, Manager, Operator];
}

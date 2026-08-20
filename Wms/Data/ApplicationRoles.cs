namespace Wms.Data;

public static class ApplicationRoles
{
    public const string Administrator = nameof(Administrator);
    public const string Operator = nameof(Operator);
    public const string WmsUser = Administrator + "," + Operator;

    public static readonly string[] All = [Administrator, Operator];
}

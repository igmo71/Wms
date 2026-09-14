namespace Wms.Data;

public static class ApplicationRoles
{
    public const string Administrator = nameof(Administrator);
    public const string Operator = nameof(Operator);
    public const string Manager = nameof(Manager);
    public const string ReceivingManager = Administrator + "," + Manager;
    public const string WmsUser = Administrator + "," + Manager + "," + Operator;

    public static readonly string[] All = [Administrator, Manager, Operator];
}

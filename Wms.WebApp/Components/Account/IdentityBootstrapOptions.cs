namespace Wms.WebApp.Components.Account;

internal sealed class IdentityBootstrapOptions
{
    public const string SectionName = "IdentityBootstrap";

    public string? AdministratorEmail { get; init; }
    public string? AdministratorDisplayName { get; init; }
    public string? AdministratorPassword { get; init; }
}

namespace Wms.Integration.OneS;

internal class OneCClientConfig
{
    public const string Section = "OneCClient";

    public required string BaseAddress { get; set; }
    public required string Username { get; set; }
    public required string Password { get; set; }
}

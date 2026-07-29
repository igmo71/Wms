namespace Wms.Integration.OneS;

public class OneCClientConfig
{
    public const string Section = "OneCClient";

    public required string BaseAddress { get; set; }
    public required string Username { get; set; }
    public required string Password { get; set; }
}

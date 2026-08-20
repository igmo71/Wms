using System.Text.Json.Serialization;

namespace Wms.Integration.OneS;

internal class OneCError
{
    [JsonPropertyName("odata.error")]
    public OdataError? OdataError { get; set; }
}

internal class OdataError
{
    [JsonPropertyName("code")]
    public string? Code { get; set; }
    [JsonPropertyName("message")]
    public Message? Message { get; set; }
}

internal class Message
{
    [JsonPropertyName("lang")]
    public string? Lang { get; set; }
    [JsonPropertyName("value")]
    public string? Value { get; set; }
}

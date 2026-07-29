using System.Text.Json.Serialization;

namespace Wms.Integration.OneS;

public class OneCError
{
    [JsonPropertyName("odata.error")]
    public OdataError? OdataError { get; set; }
}

public class OdataError
{
    [JsonPropertyName("code")]
    public string? Code { get; set; }
    [JsonPropertyName("message")]
    public Message? Message { get; set; }
}

public class Message
{
    [JsonPropertyName("lang")]
    public string? Lang { get; set; }
    [JsonPropertyName("value")]
    public string? Value { get; set; }
}
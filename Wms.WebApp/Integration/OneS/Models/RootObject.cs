using System.Text.Json.Serialization;

namespace Wms.WebApp.Integration.OneS.Models;

public class RootObject<TValue>
{
    [JsonPropertyName("odatametadata")]
    public string? OdataMetadata { get; set; }

    [JsonPropertyName("value")]
    public List<TValue>? Value { get; set; }
}

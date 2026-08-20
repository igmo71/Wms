using System.Text.Json.Serialization;

namespace Wms.Integration.OneS.Models;

internal class RootObject<TValue>
{
    [JsonPropertyName("odatametadata")]
    public string? OdataMetadata { get; set; }

    [JsonPropertyName("value")]
    public List<TValue>? Value { get; set; }
}

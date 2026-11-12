using System.Text.Json;
using System.Text.Json.Nodes;
using Wms.Application.Picking;
using Wms.Common;

namespace Wms.Integration.OneS;

public sealed class ShippingStoredSnapshotParser : IShippingStoredSnapshotParser
{
    public OperationResult<ShippingSourceSnapshot> Parse(
        Guid refKey, string storedDocumentJson) =>
        ShippingSourceParser.Parse(refKey, "{\"value\":[" + storedDocumentJson + "]}");

    public OperationResult<bool> UnchangedApartFromStatusAndPosting(
        Guid refKey, string originalDocumentJson,
        ShippingSourceSnapshot current)
    {
        var original = Parse(refKey, originalDocumentJson);
        if (!original.IsSuccess) return original.Error!;
        if (current.RefKey != refKey) return false;
        try
        {
            var before = JsonNode.Parse(original.Value!.RawDocumentJson)!.AsObject();
            var after = JsonNode.Parse(current.RawDocumentJson)!.AsObject();
            before.Remove("Статус");
            before.Remove("Posted");
            after.Remove("Статус");
            after.Remove("Posted");
            RemoveMetadata(before);
            RemoveMetadata(after);
            return JsonNode.DeepEquals(before, after);
        }
        catch (JsonException)
        {
            return OperationError.Conflict("Снимок РО 1С для возврата нечитаем.");
        }
    }

    private static void RemoveMetadata(JsonNode node)
    {
        if (node is JsonObject objectNode)
        {
            foreach (var name in objectNode.Select(x => x.Key)
                .Where(x => x.StartsWith("@odata.", StringComparison.Ordinal))
                .ToArray())
                objectNode.Remove(name);
            foreach (var child in objectNode.Select(x => x.Value).Where(x => x is not null))
                RemoveMetadata(child!);
        }
        else if (node is JsonArray array)
            foreach (var child in array.Where(x => x is not null))
                RemoveMetadata(child!);
    }
}

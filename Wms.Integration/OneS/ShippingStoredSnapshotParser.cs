using Wms.Application.Picking;
using Wms.Common;

namespace Wms.Integration.OneS;

public sealed class ShippingStoredSnapshotParser : IShippingStoredSnapshotParser
{
    public OperationResult<ShippingSourceSnapshot> Parse(
        Guid refKey, string storedDocumentJson) =>
        ShippingSourceParser.Parse(refKey, "{\"value\":[" + storedDocumentJson + "]}");
}

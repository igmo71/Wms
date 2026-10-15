using Wms.Common;

namespace Wms.Application.Receiving;

public sealed record ReceivingSourceRead(Guid RefKey, string RawResponseJson);

public interface IReceivingSourceReader
{
    Task<OperationResult<ReceivingSourceRead>> ReadAsync(Guid refKey, CancellationToken ct);
}

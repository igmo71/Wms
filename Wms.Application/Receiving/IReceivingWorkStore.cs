using Wms.Application.CommandExecution;
using Wms.Domain;

namespace Wms.Application.Receiving;

public sealed record ReceivingWorkSource(
    Guid RefKey, Guid? VersionId, string? SnapshotJson, string? LastAttemptError);

public interface IReceivingWorkStore
{
    Task<IReadOnlyList<Guid>> LoadSourceRefKeysAsync(Guid orderId, CancellationToken ct);
    Task<ReceivingOrder?> LoadOrderAsync(
        ICommandExecutionContext session, Guid orderId, CancellationToken ct);
    Task<IReadOnlyList<ReceivingWorkSource>> LoadSourcesAsync(
        ICommandExecutionContext session, Guid orderId, CancellationToken ct);
    Task<StorageLocation?> LoadLocationAsync(
        ICommandExecutionContext session, Guid locationId, CancellationToken ct);
    Task<LicensePlateNumber?> LoadLpnAsync(
        ICommandExecutionContext session, string code, CancellationToken ct);
    Task<LicensePlateNumber?> LoadLpnAsync(
        ICommandExecutionContext session, Guid lpnId, CancellationToken ct);
}

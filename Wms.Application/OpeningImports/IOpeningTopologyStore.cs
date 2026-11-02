using Wms.Application.CommandExecution;
using Wms.Common;

namespace Wms.Application.OpeningImports;

public interface IOpeningTopologyStore
{
    // Rebuilds the plan against the command session's saved state, then stages
    // all new topology nodes. The caller owns the final save and audit event.
    Task<OperationResult<OpeningTopologyPlan>> StageAsync(
        ICommandExecutionContext session,
        Guid warehouseId,
        string sourceSha256,
        OpeningExportReadResult source,
        string expectedSignature,
        CancellationToken ct);
}

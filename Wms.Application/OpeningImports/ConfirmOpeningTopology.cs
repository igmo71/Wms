using System.Security.Cryptography;
using System.Text.Json;
using Wms.Application.Authorization;
using Wms.Application.CommandExecution;
using Wms.Common;
using Wms.Domain;

namespace Wms.Application.OpeningImports;

public sealed record ConfirmOpeningTopologyCommand(
    Guid RequestId,
    Guid WarehouseId,
    string SourceName,
    ReadOnlyMemory<byte> Workbook,
    string PreviewSignature,
    string UserId);

public sealed class ConfirmOpeningTopology(
    CommandExecutor executor,
    IOpeningExportReader reader,
    IOpeningTopologyStore store,
    IWarehouseAdministratorPermissionStore permission,
    TimeProvider clock)
{
    private const string CommandType = "warehouse.opening.topology.import";

    public Task<OperationResult<Guid>> ExecuteAsync(
        ConfirmOpeningTopologyCommand command, CancellationToken ct = default)
    {
        if (command is null || command.WarehouseId == Guid.Empty
            || string.IsNullOrWhiteSpace(command.SourceName)
            || command.SourceName.Trim().Length > WarehouseOpeningImport.MaximumSourceNameLength
            || command.Workbook.IsEmpty
            || command.PreviewSignature is not { Length: 64 }
            || !command.PreviewSignature.All(Uri.IsHexDigit))
            return Task.FromResult<OperationResult<Guid>>(
                OperationError.Invalid("Для загрузки нужны склад, файл и подтверждённый предпросмотр."));

        var sourceName = command.SourceName.Trim();
        var sourceHash = Convert.ToHexString(SHA256.HashData(command.Workbook.Span));
        var previewSignature = command.PreviewSignature.ToUpperInvariant();
        var requestHash = CommandExecutor.ComputeHash(JsonSerializer.Serialize(new
        {
            command.WarehouseId, SourceName = sourceName, SourceSha256 = sourceHash,
            PreviewSignature = previewSignature
        }));

        return executor.ExecuteAsync(CommandType, command.RequestId, requestHash,
            command.UserId, async (session, operationCt) =>
            {
                if (!await permission.HasAdministratorRoleAsync(command.UserId, operationCt))
                    return OperationError.Conflict(
                        "Для загрузки топологии требуется роль Administrator.");

                var source = reader.Read(command.Workbook, operationCt);
                var staged = await store.StageAsync(session, command.WarehouseId,
                    sourceHash, source, previewSignature, operationCt);
                if (!staged.IsSuccess)
                    return staged.Error!;
                var plan = staged.Value!;
                var now = clock.GetUtcNow();
                var summary = JsonSerializer.Serialize(new
                {
                    plan.Counts,
                    Exclusions = plan.Decisions
                        .Where(x => x.Outcome == OpeningImportRowOutcome.Excluded)
                        .GroupBy(x => new { x.Source.Address, x.Reason })
                        .Select(x => new
                        {
                            x.Key.Address, x.Key.Reason,
                            Count = x.Count(),
                            Quantity = x.Sum(row => row.Source.Quantity ?? 0m),
                            Rows = x.Select(row => row.Source.RowNumber).Take(20).ToArray()
                        }).ToArray(),
                    Corrections = plan.Decisions
                        .Where(x => x.Outcome == OpeningImportRowOutcome.Corrected)
                        .Select(x => new
                        {
                            x.Source.RowNumber, SourceAddress = x.Source.Address,
                            TargetAddress = x.Address, x.Reason
                        }).Take(20).ToArray()
                });
                var created = WarehouseOpeningImport.CreateTopology(
                    command.WarehouseId, sourceName, sourceHash, summary,
                    command.UserId, now);
                if (!created.IsSuccess)
                    return created.Error!;
                var import = created.Value!;
                session.Stage(import);

                var audit = WarehouseAuditEvent.Create(
                    "warehouse.opening.topology.imported", now,
                    command.UserId, null, null, null, null, null,
                    null, JsonSerializer.Serialize(new
                    {
                        import.SourceSha256,
                        plan.Counts.SourceRows, plan.Counts.DistinctCells,
                        plan.Counts.EmptyCells, plan.Counts.NewCells,
                        plan.Counts.ExcludedRows, plan.Counts.CorrectedRows
                    }),
                    commandUserId: command.UserId,
                    commandType: CommandType,
                    requestId: command.RequestId,
                    warehouseOpeningImportId: import.Id);
                if (!audit.IsSuccess)
                    return audit.Error!;
                return new CommandExecutionResult(import.Id, [audit.Value!]);
            }, ct);
    }
}

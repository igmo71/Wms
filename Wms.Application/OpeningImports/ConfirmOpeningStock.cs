using System.Security.Cryptography;
using System.Text.Json;
using Wms.Application.Authorization;
using Wms.Application.CommandExecution;
using Wms.Common;
using Wms.Domain;

namespace Wms.Application.OpeningImports;

public sealed record ConfirmOpeningStockCommand(
    Guid RequestId,
    Guid WarehouseId,
    ReadOnlyMemory<byte> Workbook,
    DateTimeOffset DeclaredSnapshotAt,
    string PreviewSignature,
    string UserId);

public sealed class ConfirmOpeningStock(
    CommandExecutor executor,
    IOpeningExportReader reader,
    IOpeningStockConfirmationStore store,
    IWarehouseAdministratorPermissionStore permission,
    PostOpeningStock posting,
    TimeProvider clock)
{
    private const string CommandType = "warehouse.opening.stock.import";

    public Task<OperationResult<Guid>> ExecuteAsync(
        ConfirmOpeningStockCommand command, CancellationToken ct = default)
    {
        if (command is null || command.WarehouseId == Guid.Empty
            || command.Workbook.IsEmpty || command.DeclaredSnapshotAt == default
            || command.PreviewSignature is not { Length: 64 }
            || !command.PreviewSignature.All(Uri.IsHexDigit))
            return Task.FromResult<OperationResult<Guid>>(
                OperationError.Invalid("Для загрузки нужны склад, файл, время снимка и подтверждённый предпросмотр."));

        var sourceHash = Convert.ToHexString(SHA256.HashData(command.Workbook.Span));
        var previewSignature = command.PreviewSignature.ToUpperInvariant();
        var requestHash = CommandExecutor.ComputeHash(JsonSerializer.Serialize(new
        {
            command.WarehouseId, SourceSha256 = sourceHash,
            command.DeclaredSnapshotAt, PreviewSignature = previewSignature
        }));

        return executor.ExecuteAsync(CommandType, command.RequestId, requestHash,
            command.UserId, async (session, operationCt) =>
            {
                if (!await permission.HasAdministratorRoleAsync(command.UserId, operationCt))
                    return OperationError.Conflict(
                        "Для загрузки начальных остатков требуется роль Administrator.");

                var source = reader.Read(command.Workbook, operationCt);
                var skuIds = source.Rows.Where(x => x.SkuId is not null)
                    .Select(x => x.SkuId!.Value).Distinct().ToArray();
                var loaded = await store.LoadAsync(session, command.WarehouseId,
                    skuIds, operationCt);
                if (!loaded.IsSuccess)
                    return loaded.Error!;
                var state = loaded.Value!;
                var snapshotImport = state.Snapshot.Import;
                if (state.Import is null || snapshotImport is null
                    || state.Import.WarehouseId != command.WarehouseId
                    || snapshotImport.Id != state.Import.Id
                    || snapshotImport.Revision != state.Import.Revision
                    || snapshotImport.Status != state.Import.Status)
                    return OperationError.Conflict(
                        "Сначала загрузите топологию склада или обновите предпросмотр.");
                if (!string.Equals(state.Import.SourceSha256, sourceHash,
                    StringComparison.OrdinalIgnoreCase))
                    return OperationError.Conflict(
                        "Файл остатков отличается от загруженной топологии. Новая репетиция требует очистки базы.");
                if (state.Import.Status != WarehouseOpeningImportStatus.TopologyImported)
                    return OperationError.Conflict(
                        "Начальные остатки этого склада уже загружены. Проверьте сохранённый протокол; новая репетиция требует очистки базы.");

                var plan = OpeningStockPlanBuilder.Build(command.WarehouseId,
                    sourceHash, command.DeclaredSnapshotAt, source, state.Snapshot);
                if (!string.Equals(plan.Signature, previewSignature,
                    StringComparison.OrdinalIgnoreCase))
                    return OperationError.Conflict(
                        "Файл или состояние склада изменились. Обновите предпросмотр.");
                if (!plan.CanConfirm)
                    return OperationError.Invalid(
                        "Исправьте блокирующие замечания предпросмотра остатков.");
                var now = clock.GetUtcNow();
                var posted = await posting.ExecuteAsync(plan, state, state.Import.Id,
                    command.UserId, now, session, operationCt);
                if (!posted.IsSuccess)
                    return posted.Error!;

                var enabledFlags = 0;
                foreach (var skuId in plan.Rows
                    .Where(x => (x.Outcome is OpeningImportRowOutcome.Accepted
                        or OpeningImportRowOutcome.Corrected)
                        && x.RequiresExpirationDate == true)
                    .Select(x => x.StockKeepingUnitId).OfType<Guid>().Distinct())
                {
                    if (!state.Skus.TryGetValue(skuId, out var sku))
                        return OperationError.Conflict(
                            "Номенклатура изменилась после проверки снимка.");
                    if (sku.TracksExpirationDate)
                        continue;
                    sku.SetExpirationTracking(true);
                    enabledFlags++;
                }

                var reviewedMixedAddresses = ReviewedMixedAddresses(plan);
                if (reviewedMixedAddresses.Length != plan.Counts.ReviewedMixedCells)
                    return OperationError.Invalid(
                        "Состав согласованных смешанных ячеек изменился.");
                var summary = JsonSerializer.Serialize(new
                {
                    plan.SourceSha256, plan.DeclaredSnapshotAt,
                    plan.Counts.SourceRows, plan.Counts.AcceptedRows,
                    plan.Counts.CorrectedRows, plan.Counts.ExcludedRows,
                    plan.Counts.NonzeroRows, plan.Counts.ZeroRows,
                    plan.Counts.NegativeRows, plan.Counts.MissingRequiredDates,
                    plan.Counts.PastDates, plan.Counts.SignedQuantity,
                    ReviewedMixedAddresses = reviewedMixedAddresses,
                    Movements = posted.Value!.Movements,
                    Balances = posted.Value.Balances,
                    ExpirationFlagsEnabled = enabledFlags
                });
                var confirmed = state.Import.ConfirmStock(sourceHash,
                    command.DeclaredSnapshotAt, summary, command.UserId, now);
                if (!confirmed.IsSuccess)
                    return confirmed.Error!;

                var audit = WarehouseAuditEvent.Create(
                    "warehouse.opening.stock.imported", now,
                    command.UserId, null, null, null, null, null,
                    JsonSerializer.Serialize(new
                    {
                        Status = WarehouseOpeningImportStatus.TopologyImported
                    }),
                    JsonSerializer.Serialize(new
                    {
                        Status = WarehouseOpeningImportStatus.StockImported,
                        plan.SourceSha256, plan.DeclaredSnapshotAt,
                        Movements = posted.Value.Movements,
                        Balances = posted.Value.Balances,
                        ExpirationFlagsEnabled = enabledFlags,
                        ReviewedMixedAddresses = reviewedMixedAddresses
                    }),
                    commandUserId: command.UserId,
                    commandType: CommandType,
                    requestId: command.RequestId,
                    warehouseOpeningImportId: state.Import.Id);
                if (!audit.IsSuccess)
                    return audit.Error!;

                return new CommandExecutionResult(state.Import.Id, [audit.Value!]);
            }, ct);
    }

    private static string[] ReviewedMixedAddresses(OpeningStockPlan plan) =>
        plan.Rows.Where(x => (x.Outcome is OpeningImportRowOutcome.Accepted
                or OpeningImportRowOutcome.Corrected)
                && x.MixingPolicy == OpeningImportMixingPolicy.ProhibitedMixedOpeningStock
                && x.Address is not null && x.StockKeepingUnitId is not null)
            .GroupBy(x => x.Address!, StringComparer.Ordinal)
            .Where(group => group.GroupBy(x => (x.StockKeepingUnitId, x.ExpirationDate))
                .Where(skuDate => skuDate.Sum(x => x.Quantity.GetValueOrDefault()) != 0)
                .Select(x => x.Key.StockKeepingUnitId).Distinct().Take(2).Count() > 1)
            .Select(x => x.Key).OrderBy(x => x, StringComparer.Ordinal).ToArray();
}

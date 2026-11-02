using System.Text.Json;
using Wms.Common;
using Wms.Domain;

namespace Wms.Application.OpeningImports;

public sealed record OpeningImportResultSnapshot(
    Guid Id, Guid WarehouseId, string SourceName, string SourceSha256,
    WarehouseOpeningImportStatus Status,
    DateTimeOffset TopologyImportedAtUtc, string TopologyImportedBy,
    DateTimeOffset? DeclaredSnapshotAt,
    DateTimeOffset? StockImportedAtUtc, string? StockImportedBy,
    string TopologySummary, string? StockSummary);

public interface IOpeningImportResultStore
{
    Task<OpeningImportResultSnapshot?> LoadResultAsync(
        Guid warehouseId, CancellationToken ct);
}

public sealed record OpeningImportExclusion(
    string? Address, string? Reason, int Count,
    decimal Quantity, IReadOnlyList<int> Rows);

public sealed record OpeningImportCorrection(
    int RowNumber, string? SourceAddress, string? TargetAddress, string? Reason);

public sealed record OpeningStockResult(
    int SourceRows, int AcceptedRows, int CorrectedRows, int ExcludedRows,
    int NonzeroRows, int ZeroRows, int NegativeRows,
    int MissingRequiredDates, int PastDates, decimal SignedQuantity,
    IReadOnlyList<string> ReviewedMixedAddresses,
    int Movements, int Balances, int ExpirationFlagsEnabled);

public sealed record OpeningImportResult(
    Guid Id, Guid WarehouseId, string SourceName, string SourceSha256,
    WarehouseOpeningImportStatus Status,
    DateTimeOffset TopologyImportedAtUtc, string TopologyImportedBy,
    OpeningTopologyCounts TopologyCounts,
    IReadOnlyList<OpeningImportExclusion> Exclusions,
    IReadOnlyList<OpeningImportCorrection> Corrections,
    DateTimeOffset? DeclaredSnapshotAt,
    DateTimeOffset? StockImportedAtUtc, string? StockImportedBy,
    OpeningStockResult? Stock);

public sealed class GetOpeningImportResult(IOpeningImportResultStore store)
{
    public async Task<OperationResult<OpeningImportResult>> ExecuteAsync(
        Guid warehouseId, CancellationToken ct = default)
    {
        if (warehouseId == Guid.Empty)
            return OperationError.Invalid("Выберите склад для просмотра загрузки.");

        var saved = await store.LoadResultAsync(warehouseId, ct);
        if (saved is null)
            return OperationError.NotFound("Для склада нет сохранённой загрузки.");

        try
        {
            var topology = JsonSerializer.Deserialize<TopologySummary>(saved.TopologySummary);
            var stock = saved.StockSummary is null
                ? null : JsonSerializer.Deserialize<OpeningStockResult>(saved.StockSummary);
            if (topology?.Counts is null || topology.Exclusions is null
                || topology.Corrections is null
                || saved.Status == WarehouseOpeningImportStatus.StockImported &&
                    (stock?.ReviewedMixedAddresses is null
                        || saved.StockImportedAtUtc is null
                        || saved.StockImportedBy is null
                        || saved.DeclaredSnapshotAt is null))
                return OperationError.Invalid("Сохранённый результат загрузки неполон.");

            return new OpeningImportResult(saved.Id, saved.WarehouseId,
                saved.SourceName, saved.SourceSha256, saved.Status,
                saved.TopologyImportedAtUtc, saved.TopologyImportedBy,
                topology.Counts, topology.Exclusions, topology.Corrections,
                saved.DeclaredSnapshotAt, saved.StockImportedAtUtc,
                saved.StockImportedBy, stock);
        }
        catch (JsonException)
        {
            return OperationError.Invalid("Сохранённый результат загрузки повреждён.");
        }
    }

    private sealed record TopologySummary(
        OpeningTopologyCounts Counts,
        IReadOnlyList<OpeningImportExclusion> Exclusions,
        IReadOnlyList<OpeningImportCorrection> Corrections);
}

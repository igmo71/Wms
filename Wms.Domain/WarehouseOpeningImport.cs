using System.Text.Json;
using Wms.Common;

namespace Wms.Domain;

public enum WarehouseOpeningImportStatus
{
    TopologyImported = 1,
    StockImported = 2
}

public sealed class WarehouseOpeningImport
{
    public const int MaximumSourceNameLength = 260;
    public const int MaximumSummaryLength = 8000;

    private WarehouseOpeningImport() { }

    public Guid Id { get; private set; }
    public Guid WarehouseId { get; private set; }
    public Warehouse? Warehouse { get; private set; }
    public string SourceName { get; private set; } = null!;
    public string SourceSha256 { get; private set; } = null!;
    public DateTimeOffset? DeclaredSnapshotAt { get; private set; }
    public WarehouseOpeningImportStatus Status { get; private set; }
    public DateTimeOffset TopologyImportedAtUtc { get; private set; }
    public string TopologyImportedBy { get; private set; } = null!;
    public DateTimeOffset? StockImportedAtUtc { get; private set; }
    public string? StockImportedBy { get; private set; }
    public string TopologySummary { get; private set; } = null!;
    public string? StockSummary { get; private set; }
    public long Revision { get; private set; }

    public static OperationResult<WarehouseOpeningImport> CreateTopology(
        Guid warehouseId,
        string sourceName,
        string sourceSha256,
        string topologySummary,
        string userId,
        DateTimeOffset importedAtUtc)
    {
        if (warehouseId == Guid.Empty || importedAtUtc == default
            || !ValidSourceName(sourceName) || !ValidSha256(sourceSha256)
            || !ValidSummary(topologySummary) || !ValidUserId(userId))
            return OperationError.Invalid("Данные загрузки топологии неполны или некорректны.");

        return new WarehouseOpeningImport
        {
            Id = Guid.NewGuid(),
            WarehouseId = warehouseId,
            SourceName = sourceName.Trim(),
            SourceSha256 = sourceSha256.ToLowerInvariant(),
            Status = WarehouseOpeningImportStatus.TopologyImported,
            TopologyImportedAtUtc = importedAtUtc,
            TopologyImportedBy = userId.Trim(),
            TopologySummary = topologySummary
        };
    }

    public OperationResult ConfirmStock(
        string sourceSha256,
        DateTimeOffset declaredSnapshotAt,
        string stockSummary,
        string userId,
        DateTimeOffset importedAtUtc)
    {
        if (Status != WarehouseOpeningImportStatus.TopologyImported)
            return OperationError.Conflict("Начальные остатки уже загружены для этого склада.");
        if (!ValidSha256(sourceSha256)
            || !string.Equals(SourceSha256, sourceSha256, StringComparison.OrdinalIgnoreCase))
            return OperationError.Conflict("Файл остатков отличается от файла топологии.");
        if (declaredSnapshotAt == default || importedAtUtc == default
            || !ValidSummary(stockSummary) || !ValidUserId(userId))
            return OperationError.Invalid("Данные загрузки начальных остатков неполны или некорректны.");

        DeclaredSnapshotAt = declaredSnapshotAt;
        StockImportedAtUtc = importedAtUtc;
        StockImportedBy = userId.Trim();
        StockSummary = stockSummary;
        Status = WarehouseOpeningImportStatus.StockImported;
        Revision++;
        return OperationResult.Success();
    }

    private static bool ValidSourceName(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= MaximumSourceNameLength;

    private static bool ValidSha256(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static bool ValidUserId(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= 36;

    private static bool ValidSummary(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > MaximumSummaryLength)
            return false;
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

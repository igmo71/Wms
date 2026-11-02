namespace Wms.Application.OpeningImports;

// Source text is retained alongside parsed values so an invalid value can be
// reported without treating it as an absent one.
public sealed record OpeningExportRow
{
    public required int RowNumber { get; init; }

    public string? Area { get; init; }
    public string? CellGroup { get; init; }
    public string? Address { get; init; }
    public string? CellKind { get; init; }

    public string? SkuName { get; init; }
    public string? SourceSkuGuid { get; init; }
    public Guid? SkuId { get; init; }
    public string? UnitOfMeasure { get; init; }
    public string? SourceQuantity { get; init; }
    public decimal? Quantity { get; init; }
    public string? SourceDate { get; init; }
    public DateOnly? ExpirationDate { get; init; }
    public string? SourceDateRequirement { get; init; }
    public bool? RequiresExpirationDate { get; init; }
}

public sealed record OpeningExportParseIssue(
    int? RowNumber,
    string? ColumnName,
    string? SourceValue,
    string Message);

public sealed record OpeningExportReadResult(
    IReadOnlyList<OpeningExportRow> Rows,
    IReadOnlyList<OpeningExportParseIssue> Issues);

public interface IOpeningExportReader
{
    OpeningExportReadResult Read(ReadOnlyMemory<byte> workbook, CancellationToken ct);
}

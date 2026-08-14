using Wms.Common;

namespace Wms.Application.Reports.EmployeePerformance;

public sealed class EmployeePerformanceSummaryQuery : ListQuery
{
    public Guid? WarehouseId { get; set; }
    public string? UserId { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
}

public sealed class EmployeePerformanceDetailsQuery : ListQuery
{
    public required string UserId { get; set; }
    public Guid? WarehouseId { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public EmployeePerformanceDocumentType? DocumentType { get; set; }
}

public sealed class EmployeePerformanceUserSearchQuery
{
    public string? SearchString { get; set; }
    public Guid? WarehouseId { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public int Take { get; set; } = 10;
}

public sealed class EmployeePerformanceSummaryItem
{
    public Guid WarehouseId { get; init; }
    public required string WarehouseName { get; init; }
    public required string UserId { get; init; }
    public required string UserName { get; init; }
    public int DocumentCount { get; init; }
    public int PositiveLineCount { get; init; }
    public double KnownFactWeightKg { get; init; }
    public bool IsFactWeightComplete { get; init; }
}

public sealed class EmployeePerformanceTotals
{
    public int DocumentCount { get; init; }
    public int PositiveLineCount { get; init; }
    public double KnownFactWeightKg { get; init; }
    public bool IsFactWeightComplete { get; init; }
}

public sealed class EmployeePerformanceSummaryResult
{
    public List<EmployeePerformanceSummaryItem> Items { get; init; } = [];
    public int TotalItems { get; init; }
    public EmployeePerformanceTotals Totals { get; init; } = new();
}

public sealed class EmployeePerformanceDocumentItem
{
    public EmployeePerformanceDocumentType DocumentType { get; init; }
    public Guid DocumentId { get; init; }
    public string? Number { get; init; }
    public DateTime DocumentDate { get; init; }
    public Guid WarehouseId { get; init; }
    public required string WarehouseName { get; init; }
    public int PositiveLineCount { get; init; }
    public double KnownFactWeightKg { get; init; }
    public bool IsFactWeightComplete { get; init; }
    public DateTimeOffset StartedAtUtc { get; init; }
    public DateTimeOffset CompletedAtUtc { get; init; }
    public double DurationMinutes { get; init; }
}

public sealed class EmployeePerformanceDetailsResult
{
    public List<EmployeePerformanceDocumentItem> Items { get; init; } = [];
    public int TotalItems { get; init; }
}

public sealed class EmployeePerformanceUserOption
{
    public required string Id { get; init; }
    public required string Name { get; init; }
}

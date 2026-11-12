using Microsoft.EntityFrameworkCore;
using Wms.Data;
using Wms.Domain.Enums;

namespace Wms.Application.Reports.EmployeePerformance;

public sealed class EmployeePerformanceReportService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory)
{
    public async Task<EmployeePerformanceSummaryResult> GetSummaryAsync(
        EmployeePerformanceSummaryQuery reportQuery,
        CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var documents = ApplyCommonFilters(BuildDocuments(dbContext), reportQuery);

        var summaryQuery = documents
            .GroupBy(x => new
            {
                x.WarehouseId,
                x.WarehouseName,
                x.UserId,
                x.UserName
            })
            .Select(group => new EmployeePerformanceSummaryItem
            {
                WarehouseId = group.Key.WarehouseId,
                WarehouseName = group.Key.WarehouseName,
                UserId = group.Key.UserId,
                UserName = group.Key.UserName,
                DocumentCount = group.Count(),
                PositiveLineCount = group.Sum(x => x.PositiveLineCount),
                KnownFactWeightKg = group.Sum(x => x.KnownFactWeightKg),
                IsFactWeightComplete = !group.Any(x => !x.IsFactWeightComplete)
            });

        var totalItems = await summaryQuery.CountAsync(ct);
        var documentCount = await documents.CountAsync(ct);
        var positiveLineCount = await documents
            .SumAsync(x => (int?)x.PositiveLineCount, ct) ?? 0;
        var knownFactWeightKg = await documents
            .SumAsync(x => (double?)x.KnownFactWeightKg, ct) ?? 0;
        var isFactWeightComplete = !await documents
            .AnyAsync(x => !x.IsFactWeightComplete, ct);

        var items = await ApplySummarySorting(
                summaryQuery,
                reportQuery.SortBy,
                reportQuery.SortDescending)
            .Skip(reportQuery.Skip)
            .Take(reportQuery.Take)
            .ToListAsync(ct);

        return new EmployeePerformanceSummaryResult
        {
            Items = items,
            TotalItems = totalItems,
            Totals = new EmployeePerformanceTotals
            {
                DocumentCount = documentCount,
                PositiveLineCount = positiveLineCount,
                KnownFactWeightKg = knownFactWeightKg,
                IsFactWeightComplete = isFactWeightComplete
            }
        };
    }

    public async Task<EmployeePerformanceDetailsResult> GetDetailsAsync(
        EmployeePerformanceDetailsQuery reportQuery,
        CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var commonQuery = new EmployeePerformanceSummaryQuery
        {
            WarehouseId = reportQuery.WarehouseId,
            UserId = reportQuery.UserId,
            DateFrom = reportQuery.DateFrom,
            DateTo = reportQuery.DateTo
        };

        var documents = ApplyCommonFilters(BuildDocuments(dbContext), commonQuery);

        if (reportQuery.DocumentType is EmployeePerformanceDocumentType documentType)
            documents = documents.Where(x => x.DocumentType == documentType);

        if (!string.IsNullOrWhiteSpace(reportQuery.SearchString))
            documents = documents.Where(x => x.Number != null
                && x.Number.Contains(reportQuery.SearchString));

        var totalItems = await documents.CountAsync(ct);

        var projected = documents.Select(x => new EmployeePerformanceDocumentItem
        {
            DocumentType = x.DocumentType,
            DocumentId = x.DocumentId,
            Number = x.Number,
            DocumentDate = x.DocumentDate,
            WarehouseId = x.WarehouseId,
            WarehouseName = x.WarehouseName,
            PositiveLineCount = x.PositiveLineCount,
            KnownFactWeightKg = x.KnownFactWeightKg,
            IsFactWeightComplete = x.IsFactWeightComplete,
            StartedAtUtc = x.StartedAtUtc,
            CompletedAtUtc = x.CompletedAtUtc,
            DurationMinutes = EF.Functions.DateDiffSecond(x.StartedAtUtc, x.CompletedAtUtc) / 60.0
        });

        var items = await ApplyDetailsSorting(
                projected,
                reportQuery.SortBy,
                reportQuery.SortDescending)
            .Skip(reportQuery.Skip)
            .Take(reportQuery.Take)
            .ToListAsync(ct);

        return new EmployeePerformanceDetailsResult
        {
            Items = items,
            TotalItems = totalItems
        };
    }

    public async Task<List<EmployeePerformanceUserOption>> SearchUsersAsync(
        EmployeePerformanceUserSearchQuery searchQuery,
        CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var documents = ApplyCommonFilters(BuildDocuments(dbContext), new EmployeePerformanceSummaryQuery
        {
            WarehouseId = searchQuery.WarehouseId,
            DateFrom = searchQuery.DateFrom,
            DateTo = searchQuery.DateTo
        });

        if (!string.IsNullOrWhiteSpace(searchQuery.SearchString))
            documents = documents.Where(x => x.UserName.Contains(searchQuery.SearchString)
                || x.UserId.Contains(searchQuery.SearchString));

        return await documents
            .GroupBy(x => new { x.UserId, x.UserName })
            .Select(group => new EmployeePerformanceUserOption
            {
                Id = group.Key.UserId,
                Name = group.Key.UserName
            })
            .OrderBy(x => x.Name)
            .ThenBy(x => x.Id)
            .Take(searchQuery.Take)
            .ToListAsync(ct);
    }

    public async Task<string> GetUserNameAsync(string userId, CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var userName = await dbContext.Users
            .AsNoTracking()
            .Where(x => x.Id == userId)
            .Select(x => string.IsNullOrWhiteSpace(x.DisplayName)
                ? x.UserName
                : x.DisplayName)
            .SingleOrDefaultAsync(ct);

        return userName ?? GetDeletedUserName(userId);
    }

    private static IQueryable<EmployeePerformanceDocumentProjection> BuildDocuments(
        ApplicationDbContext dbContext)
    {
        var facts =
            from entry in dbContext.PickingEntries.AsNoTracking()
            where entry.Quantity > 0
            join assembly in dbContext.PickingAssemblies.AsNoTracking()
                on entry.PickingAssemblyId equals assembly.Id
            where assembly.Status == PickingAssemblyStatus.Closed
            join warehouse in dbContext.Warehouses.AsNoTracking()
                on assembly.WarehouseId equals warehouse.Id
            join sku in dbContext.StockKeepingUnits.AsNoTracking()
                on entry.StockKeepingUnitId equals sku.Id
            group new { entry.Quantity, entry.ConfirmedAtUtc, sku.WeightKg } by new
            {
                assembly.Id, assembly.Number, assembly.CreatedAtUtc,
                assembly.WarehouseId, WarehouseName = warehouse.Name,
                entry.ConfirmedBy
            } into groupByWorker
            select new
            {
                groupByWorker.Key.Id,
                groupByWorker.Key.Number,
                groupByWorker.Key.CreatedAtUtc,
                groupByWorker.Key.WarehouseId,
                groupByWorker.Key.WarehouseName,
                groupByWorker.Key.ConfirmedBy,
                PositiveLineCount = groupByWorker.Count(),
                KnownFactWeightKg = groupByWorker.Sum(x => x.WeightKg != null
                    ? (double)x.Quantity * x.WeightKg.Value : 0d),
                IsFactWeightComplete = groupByWorker.Count(x => x.WeightKg == null) == 0,
                StartedAtUtc = groupByWorker.Min(x => x.ConfirmedAtUtc),
                CompletedAtUtc = groupByWorker.Max(x => x.ConfirmedAtUtc)
            };

        var picking =
            from fact in facts
            from user in dbContext.Users
                .Where(x => x.Id == fact.ConfirmedBy).DefaultIfEmpty()
            select new EmployeePerformanceDocumentProjection
            {
                DocumentType = EmployeePerformanceDocumentType.Picking,
                DocumentId = fact.Id,
                Number = fact.Number,
                DocumentDate = fact.CreatedAtUtc.UtcDateTime,
                WarehouseId = fact.WarehouseId,
                WarehouseName = fact.WarehouseName,
                UserId = fact.ConfirmedBy,
                UserName = user != null && !string.IsNullOrWhiteSpace(user.DisplayName)
                    ? user.DisplayName
                    : user != null && user.UserName != null
                    ? user.UserName
                    : "Удаленный пользователь (" + fact.ConfirmedBy + ")",
                PositiveLineCount = fact.PositiveLineCount,
                KnownFactWeightKg = fact.KnownFactWeightKg,
                IsFactWeightComplete = fact.IsFactWeightComplete,
                StartedAtUtc = fact.StartedAtUtc,
                CompletedAtUtc = fact.CompletedAtUtc
            };

        return picking;
    }

    private static IQueryable<EmployeePerformanceDocumentProjection> ApplyCommonFilters(
        IQueryable<EmployeePerformanceDocumentProjection> query,
        EmployeePerformanceSummaryQuery reportQuery)
    {
        if (reportQuery.WarehouseId is Guid warehouseId)
            query = query.Where(x => x.WarehouseId == warehouseId);

        if (!string.IsNullOrWhiteSpace(reportQuery.UserId))
            query = query.Where(x => x.UserId == reportQuery.UserId);

        if (ToUtc(reportQuery.DateFrom) is DateTimeOffset dateFromUtc)
            query = query.Where(x => x.CompletedAtUtc >= dateFromUtc);

        if (ToUtc(reportQuery.DateTo?.Date.AddDays(1)) is DateTimeOffset dateToUtc)
            query = query.Where(x => x.CompletedAtUtc < dateToUtc);

        return query;
    }

    private static IOrderedQueryable<EmployeePerformanceSummaryItem> ApplySummarySorting(
        IQueryable<EmployeePerformanceSummaryItem> query,
        string? sortBy,
        bool sortDescending)
    {
        return sortBy switch
        {
            "WarehouseName" => sortDescending
                ? query.OrderByDescending(x => x.WarehouseName).ThenBy(x => x.UserName).ThenBy(x => x.UserId)
                : query.OrderBy(x => x.WarehouseName).ThenBy(x => x.UserName).ThenBy(x => x.UserId),
            "UserName" => sortDescending
                ? query.OrderByDescending(x => x.UserName).ThenBy(x => x.WarehouseName).ThenBy(x => x.UserId)
                : query.OrderBy(x => x.UserName).ThenBy(x => x.WarehouseName).ThenBy(x => x.UserId),
            "DocumentCount" => sortDescending
                ? query.OrderByDescending(x => x.DocumentCount).ThenBy(x => x.WarehouseName).ThenBy(x => x.UserName)
                : query.OrderBy(x => x.DocumentCount).ThenBy(x => x.WarehouseName).ThenBy(x => x.UserName),
            "PositiveLineCount" => sortDescending
                ? query.OrderByDescending(x => x.PositiveLineCount).ThenBy(x => x.WarehouseName).ThenBy(x => x.UserName)
                : query.OrderBy(x => x.PositiveLineCount).ThenBy(x => x.WarehouseName).ThenBy(x => x.UserName),
            "KnownFactWeightKg" => sortDescending
                ? query.OrderByDescending(x => x.KnownFactWeightKg).ThenBy(x => x.WarehouseName).ThenBy(x => x.UserName)
                : query.OrderBy(x => x.KnownFactWeightKg).ThenBy(x => x.WarehouseName).ThenBy(x => x.UserName),
            _ => query.OrderByDescending(x => x.DocumentCount)
                .ThenBy(x => x.WarehouseName)
                .ThenBy(x => x.UserName)
        };
    }

    private static IOrderedQueryable<EmployeePerformanceDocumentItem> ApplyDetailsSorting(
        IQueryable<EmployeePerformanceDocumentItem> query,
        string? sortBy,
        bool sortDescending)
    {
        return sortBy switch
        {
            "DocumentType" => sortDescending
                ? query.OrderByDescending(x => x.DocumentType).ThenByDescending(x => x.CompletedAtUtc).ThenBy(x => x.DocumentId)
                : query.OrderBy(x => x.DocumentType).ThenByDescending(x => x.CompletedAtUtc).ThenBy(x => x.DocumentId),
            "Number" => sortDescending
                ? query.OrderByDescending(x => x.Number).ThenByDescending(x => x.CompletedAtUtc).ThenBy(x => x.DocumentId)
                : query.OrderBy(x => x.Number).ThenByDescending(x => x.CompletedAtUtc).ThenBy(x => x.DocumentId),
            "DocumentDate" => sortDescending
                ? query.OrderByDescending(x => x.DocumentDate).ThenByDescending(x => x.CompletedAtUtc).ThenBy(x => x.DocumentId)
                : query.OrderBy(x => x.DocumentDate).ThenByDescending(x => x.CompletedAtUtc).ThenBy(x => x.DocumentId),
            "WarehouseName" => sortDescending
                ? query.OrderByDescending(x => x.WarehouseName).ThenByDescending(x => x.CompletedAtUtc).ThenBy(x => x.DocumentId)
                : query.OrderBy(x => x.WarehouseName).ThenByDescending(x => x.CompletedAtUtc).ThenBy(x => x.DocumentId),
            "PositiveLineCount" => sortDescending
                ? query.OrderByDescending(x => x.PositiveLineCount).ThenByDescending(x => x.CompletedAtUtc).ThenBy(x => x.DocumentId)
                : query.OrderBy(x => x.PositiveLineCount).ThenByDescending(x => x.CompletedAtUtc).ThenBy(x => x.DocumentId),
            "KnownFactWeightKg" => sortDescending
                ? query.OrderByDescending(x => x.KnownFactWeightKg).ThenByDescending(x => x.CompletedAtUtc).ThenBy(x => x.DocumentId)
                : query.OrderBy(x => x.KnownFactWeightKg).ThenByDescending(x => x.CompletedAtUtc).ThenBy(x => x.DocumentId),
            "StartedAtUtc" => sortDescending
                ? query.OrderByDescending(x => x.StartedAtUtc).ThenBy(x => x.DocumentId)
                : query.OrderBy(x => x.StartedAtUtc).ThenBy(x => x.DocumentId),
            "CompletedAtUtc" => sortDescending
                ? query.OrderByDescending(x => x.CompletedAtUtc).ThenBy(x => x.DocumentId)
                : query.OrderBy(x => x.CompletedAtUtc).ThenBy(x => x.DocumentId),
            "DurationMinutes" => sortDescending
                ? query.OrderByDescending(x => x.DurationMinutes).ThenByDescending(x => x.CompletedAtUtc).ThenBy(x => x.DocumentId)
                : query.OrderBy(x => x.DurationMinutes).ThenByDescending(x => x.CompletedAtUtc).ThenBy(x => x.DocumentId),
            _ => query.OrderByDescending(x => x.CompletedAtUtc).ThenBy(x => x.DocumentId)
        };
    }

    private static DateTimeOffset? ToUtc(DateTime? localDate)
    {
        if (localDate is null)
            return null;

        var unspecified = DateTime.SpecifyKind(localDate.Value, DateTimeKind.Unspecified);
        var utc = TimeZoneInfo.ConvertTimeToUtc(unspecified, TimeZoneInfo.Local);
        return new DateTimeOffset(utc);
    }

    private static string GetDeletedUserName(string userId) =>
        $"Удаленный пользователь ({userId})";

    private sealed class EmployeePerformanceDocumentProjection
    {
        public EmployeePerformanceDocumentType DocumentType { get; init; }
        public Guid DocumentId { get; init; }
        public string? Number { get; init; }
        public DateTime DocumentDate { get; init; }
        public Guid WarehouseId { get; init; }
        public required string WarehouseName { get; init; }
        public required string UserId { get; init; }
        public required string UserName { get; init; }
        public int PositiveLineCount { get; init; }
        public double KnownFactWeightKg { get; init; }
        public bool IsFactWeightComplete { get; init; }
        public DateTimeOffset StartedAtUtc { get; init; }
        public DateTimeOffset CompletedAtUtc { get; init; }
    }
}

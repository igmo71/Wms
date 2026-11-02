using System.Security.Cryptography;
using System.Text.Json;
using Wms.Common;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Application.OpeningImports;

public sealed record OpeningStockExistingImport(
    Guid Id, string SourceSha256, WarehouseOpeningImportStatus Status, long Revision);

public sealed record OpeningStockLocation(
    Guid Id, string Address, ZoneType? Purpose, bool DeletionMark, bool IsFolder,
    bool ZoneDeleted, bool HasActiveLock, bool AllowsMultipleSkus, long OperationalRevision);

public sealed record OpeningStockSku(
    Guid Id, string Name, string? BaseUnitName, string? BaseUnitDescription,
    string? BaseUnitAbbreviation, bool DeletionMark,
    bool BaseUnitDeleted, bool TracksExpirationDate);

public sealed record OpeningStockImportState(
    bool WarehouseExists, bool WarehouseDeleted, OpeningStockExistingImport? Import,
    IReadOnlyList<OpeningStockLocation> Locations, IReadOnlyList<OpeningStockSku> Skus,
    bool HasOperationalStock, bool HasOperationalWork);

public interface IOpeningStockStateStore
{
    Task<OpeningStockImportState> LoadAsync(
        Guid warehouseId, IReadOnlyCollection<Guid> skuIds, CancellationToken ct);
}

public sealed record OpeningStockRowPlan(
    int RowNumber, string? SourceAddress, string? Address, ZoneType? Purpose,
    Guid? StockKeepingUnitId, decimal? Quantity, DateOnly? ExpirationDate,
    bool? RequiresExpirationDate, OpeningImportMixingPolicy MixingPolicy,
    OpeningImportRowOutcome Outcome, string? Reason);

public sealed record OpeningStockTotal(
    Guid StockKeepingUnitId, string SkuName, ZoneType Purpose,
    decimal SignedQuantity, int Rows);

public sealed record OpeningStockIssue(
    int? RowNumber, string? Address, string Message, bool BlocksConfirmation);

public sealed record OpeningStockCounts(
    int SourceRows, int AcceptedRows, int CorrectedRows, int ExcludedRows,
    int BlockingRows, int SourceStockRows, int AcceptedStockRows,
    int NonzeroRows, int ZeroRows,
    int NegativeRows, int MissingRequiredDates, int PastDates,
    int ReviewedMixedCells, int UnreviewedMixedCells,
    decimal SignedQuantity, decimal ExcludedQuantity,
    int BlockingIssues, int InformationalIssues);

public sealed record OpeningStockPreview(
    Guid WarehouseId, string SourceSha256, string Signature,
    DateTimeOffset DeclaredSnapshotAt, bool CanConfirm, OpeningStockCounts Counts,
    OpeningTopologyPage<OpeningStockRowPlan> Rows,
    OpeningTopologyPage<OpeningStockTotal> Totals,
    OpeningTopologyPage<OpeningStockIssue> Issues);

public sealed record OpeningStockPlan(
    Guid WarehouseId, string SourceSha256, string Signature,
    DateTimeOffset DeclaredSnapshotAt,
    IReadOnlyList<OpeningStockRowPlan> Rows,
    IReadOnlyList<OpeningStockTotal> Totals,
    IReadOnlyList<OpeningStockIssue> Issues,
    OpeningStockCounts Counts)
{
    public bool CanConfirm => Counts.BlockingIssues == 0;

    public OpeningStockPreview Page(int page = 1, int pageSize = 100) =>
        new(WarehouseId, SourceSha256, Signature, DeclaredSnapshotAt, CanConfirm,
            Counts, Slice(Rows, page, pageSize), Slice(Totals, page, pageSize),
            Slice(Issues, page, pageSize));

    private static OpeningTopologyPage<T> Slice<T>(
        IReadOnlyList<T> source, int page, int pageSize)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var offset = (long)(page - 1) * pageSize;
        return new OpeningTopologyPage<T>(
            offset >= source.Count ? [] : source.Skip((int)offset).Take(pageSize).ToArray(),
            source.Count, page, pageSize);
    }
}

public sealed class PreviewOpeningStock(
    IOpeningExportReader reader, IOpeningStockStateStore store)
{
    public async Task<OpeningStockPreview> ExecuteAsync(
        Guid warehouseId, ReadOnlyMemory<byte> workbook,
        DateTimeOffset declaredSnapshotAt, int page = 1, int pageSize = 100,
        CancellationToken ct = default)
    {
        var plan = await BuildPlanAsync(warehouseId, workbook, declaredSnapshotAt, ct);
        return plan.Page(page, pageSize);
    }

    public async Task<OpeningStockPlan> BuildPlanAsync(
        Guid warehouseId, ReadOnlyMemory<byte> workbook,
        DateTimeOffset declaredSnapshotAt, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(warehouseId, Guid.Empty);
        var source = reader.Read(workbook, ct);
        var skuIds = source.Rows.Where(x => x.SkuId is not null)
            .Select(x => x.SkuId!.Value).Distinct().ToArray();
        var state = await store.LoadAsync(warehouseId, skuIds, ct);
        var sourceHash = Convert.ToHexString(SHA256.HashData(workbook.Span));
        return OpeningStockPlanBuilder.Build(
            warehouseId, sourceHash, declaredSnapshotAt, source, state);
    }
}

public static class OpeningStockPlanBuilder
{
    public static OpeningStockPlan Build(
        Guid warehouseId, string sourceSha256, DateTimeOffset declaredSnapshotAt,
        OpeningExportReadResult source, OpeningStockImportState state)
    {
        var issues = source.Issues.Select(x => new OpeningStockIssue(
            x.RowNumber, null,
            x.ColumnName is null ? x.Message : $"{x.ColumnName}: {x.Message}", true)).ToList();
        var rowsWithParseIssues = source.Issues.Where(x => x.RowNumber is > 1)
            .Select(x => x.RowNumber!.Value).ToHashSet();
        if (!state.WarehouseExists || state.WarehouseDeleted)
            issues.Add(new(null, null, "Склад не найден или отключён.", true));
        if (declaredSnapshotAt == default)
            issues.Add(new(null, null, "Укажите момент выгрузки начальных остатков.", true));
        if (state.Import is null)
            issues.Add(new(null, null, "Сначала загрузите топологию склада.", true));
        else
        {
            if (state.Import.Status != WarehouseOpeningImportStatus.TopologyImported)
                issues.Add(new(null, null, "Начальные остатки уже загружены для этого склада.", true));
            if (!string.Equals(state.Import.SourceSha256, sourceSha256,
                    StringComparison.OrdinalIgnoreCase))
                issues.Add(new(null, null, "Файл остатков отличается от файла топологии.", true));
        }
        if (state.HasOperationalStock)
            issues.Add(new(null, null,
                "На складе уже есть движения или остатки; загрузка начального снимка запрещена.", true));
        if (state.HasOperationalWork)
            issues.Add(new(null, null,
                "На складе уже есть операционная работа; загрузка начального снимка запрещена.", true));

        var locations = state.Locations.ToDictionary(x => x.Address, StringComparer.Ordinal);
        var skus = state.Skus.ToDictionary(x => x.Id);
        var decisions = new List<OpeningStockRowPlan>(source.Rows.Count);
        var runningBalances = new Dictionary<(Guid LocationId, Guid SkuId, DateOnly? Date), decimal>();
        var validStockRows = new List<(OpeningStockRowPlan Row, OpeningStockLocation Location,
            OpeningStockSku Sku)>();
        var snapshotDate = declaredSnapshotAt == default
            ? DateOnly.MinValue : DateOnly.FromDateTime(declaredSnapshotAt.Date);
        var missingRequiredDates = 0;
        var pastDates = 0;
        decimal excludedQuantity = 0;

        foreach (var sourceRow in source.Rows)
        {
            var interpreted = OpeningImportPolicy.Interpret(sourceRow);
            var outcome = interpreted.Outcome;
            var reason = interpreted.Reason;
            var hasStock = HasStockPayload(sourceRow);
            if (rowsWithParseIssues.Contains(sourceRow.RowNumber))
            {
                outcome = OpeningImportRowOutcome.Blocking;
                reason = "Ошибка чтения строки выгрузки.";
            }
            else if (outcome == OpeningImportRowOutcome.Excluded)
            {
                if (sourceRow.Quantity is decimal excluded)
                    excludedQuantity += excluded;
                issues.Add(new(sourceRow.RowNumber, sourceRow.Address, reason!, false));
            }
            else if (outcome == OpeningImportRowOutcome.Blocking)
                issues.Add(new(sourceRow.RowNumber, sourceRow.Address, reason!, true));
            else if (outcome == OpeningImportRowOutcome.Corrected)
                issues.Add(new(sourceRow.RowNumber, sourceRow.Address, reason!, false));

            OpeningStockLocation? location = null;
            OpeningStockSku? sku = null;
            if (hasStock && outcome is
                (OpeningImportRowOutcome.Accepted or OpeningImportRowOutcome.Corrected))
            {
                string? validationError = null;
                var quantity = sourceRow.Quantity.GetValueOrDefault();
                if (sourceRow.SkuId is not Guid skuId || sourceRow.Quantity is null
                    || string.IsNullOrWhiteSpace(sourceRow.UnitOfMeasure)
                    || sourceRow.RequiresExpirationDate is null)
                    validationError = "Строке с товаром нужны GUID, единица, количество и признак даты.";
                else if (!WarehouseQuantity.IsSupported(quantity))
                    validationError = "Количество должно укладываться в decimal(15,3).";
                else if (sourceRow.SourceDate is not null && sourceRow.ExpirationDate is null)
                    validationError = "Дата товара не распознана.";
                else if (!locations.TryGetValue(interpreted.Address!, out location))
                    validationError = "Адрес отсутствует в загруженной топологии.";
                else if (location.DeletionMark || location.IsFolder || location.ZoneDeleted
                    || location.HasActiveLock || location.Purpose != interpreted.Purpose)
                    validationError = "Ячейка отключена, заблокирована или имеет другое назначение.";
                else if (location.AllowsMultipleSkus !=
                    (interpreted.MixingPolicy == OpeningImportMixingPolicy.ApprovedMultipleSkus))
                    validationError = "Разрешение нескольких SKU отличается от согласованной топологии.";
                else if (!skus.TryGetValue(skuId, out sku))
                    validationError = "Номенклатура отсутствует в каталоге WMS.";
                else if (sku.DeletionMark)
                    validationError = "Номенклатура помечена на удаление в каталоге 1С.";
                else if (sku.BaseUnitDeleted)
                    validationError = "Базовая единица номенклатуры отсутствует или отключена.";
                else if (string.IsNullOrWhiteSpace(sku.BaseUnitName)
                    && string.IsNullOrWhiteSpace(sku.BaseUnitDescription)
                    && string.IsNullOrWhiteSpace(sku.BaseUnitAbbreviation))
                    validationError = "У базовой единицы номенклатуры нет названия или сокращения.";
                else if (!MatchesBaseUnit(sourceRow.UnitOfMeasure, sku))
                    validationError = "Единица выгрузки не совпадает с базовой единицей SKU.";
                else
                {
                    var key = (location.Id, sku.Id, sourceRow.ExpirationDate);
                    var next = runningBalances.GetValueOrDefault(key) + quantity;
                    if (!WarehouseQuantity.IsSupported(next))
                        validationError = "Суммарный остаток ячейки, SKU и даты выходит за decimal(15,3).";
                    else
                        runningBalances[key] = next;
                }

                if (validationError is not null)
                {
                    outcome = OpeningImportRowOutcome.Blocking;
                    reason = validationError;
                    issues.Add(new(sourceRow.RowNumber, sourceRow.Address, validationError, true));
                }
                else
                {
                    if (quantity < 0)
                        issues.Add(new(sourceRow.RowNumber, interpreted.Address,
                            "Отрицательный остаток будет сохранён со знаком.", false));
                    if (sourceRow.ExpirationDate is null
                        && (sourceRow.RequiresExpirationDate == true || sku!.TracksExpirationDate))
                    {
                        missingRequiredDates++;
                        issues.Add(new(sourceRow.RowNumber, interpreted.Address,
                            "Для SKU требуется дата, но в выгрузке она отсутствует; останется неизвестной.", false));
                    }
                    if (sourceRow.ExpirationDate is DateOnly date && date < snapshotDate)
                    {
                        pastDates++;
                        issues.Add(new(sourceRow.RowNumber, interpreted.Address,
                            "Дата товара раньше момента снимка; будет сохранена как указано.", false));
                    }
                }
            }

            var row = new OpeningStockRowPlan(sourceRow.RowNumber, sourceRow.Address,
                interpreted.Address, interpreted.Purpose, sourceRow.SkuId,
                sourceRow.Quantity, sourceRow.ExpirationDate,
                sourceRow.RequiresExpirationDate, interpreted.MixingPolicy, outcome, reason);
            decisions.Add(row);
            if (hasStock && outcome is
                (OpeningImportRowOutcome.Accepted or OpeningImportRowOutcome.Corrected))
                validStockRows.Add((row, location!, sku!));
        }

        var reviewedMixedCells = 0;
        var unreviewedMixedCells = 0;
        var firstStockRowByLocation = validStockRows.GroupBy(x => x.Location.Id)
            .ToDictionary(x => x.Key, x => x.First());
        foreach (var group in runningBalances.Where(x => x.Value != 0)
            .GroupBy(x => x.Key.LocationId))
        {
            if (group.Select(x => x.Key.SkuId).Distinct().Take(2).Count() <= 1)
                continue;
            var first = firstStockRowByLocation[group.Key];
            var location = first.Location;
            if (location.Purpose is not (ZoneType.Storage or ZoneType.Reserve))
                continue;
            if (first.Row.MixingPolicy == OpeningImportMixingPolicy.ProhibitedMixedOpeningStock
                && !location.AllowsMultipleSkus)
            {
                reviewedMixedCells++;
                issues.Add(new(null, location.Address,
                    "Согласованная ячейка с несколькими SKU при запрете смешения; исходный факт сохранится.", false));
            }
            else if (first.Row.MixingPolicy != OpeningImportMixingPolicy.ApprovedMultipleSkus)
            {
                unreviewedMixedCells++;
                issues.Add(new(null, location.Address,
                    "Несколько SKU в ячейке вне согласованного списка; требуется решение.", true));
            }
        }

        var totals = validStockRows.GroupBy(x => (x.Sku.Id, x.Location.Purpose))
            .Select(x => new OpeningStockTotal(x.Key.Id, x.First().Sku.Name,
                x.Key.Purpose!.Value, x.Sum(row => row.Row.Quantity ?? 0), x.Count()))
            .OrderBy(x => x.SkuName, StringComparer.Ordinal)
            .ThenBy(x => x.Purpose).ThenBy(x => x.StockKeepingUnitId).ToArray();
        var orderedIssues = issues.OrderBy(x => x.RowNumber ?? int.MaxValue)
            .ThenBy(x => x.Address, StringComparer.Ordinal).ToArray();
        var counts = new OpeningStockCounts(
            decisions.Count, decisions.Count(x => x.Outcome == OpeningImportRowOutcome.Accepted),
            decisions.Count(x => x.Outcome == OpeningImportRowOutcome.Corrected),
            decisions.Count(x => x.Outcome == OpeningImportRowOutcome.Excluded),
            decisions.Count(x => x.Outcome == OpeningImportRowOutcome.Blocking),
            source.Rows.Count(HasStockPayload), validStockRows.Count,
            validStockRows.Count(x => x.Row.Quantity != 0),
            validStockRows.Count(x => x.Row.Quantity == 0),
            validStockRows.Count(x => x.Row.Quantity < 0), missingRequiredDates, pastDates,
            reviewedMixedCells, unreviewedMixedCells,
            validStockRows.Sum(x => x.Row.Quantity ?? 0), excludedQuantity,
            orderedIssues.Count(x => x.BlocksConfirmation),
            orderedIssues.Count(x => !x.BlocksConfirmation));
        var signature = Sign(warehouseId, sourceSha256, declaredSnapshotAt,
            state, decisions, counts);
        return new OpeningStockPlan(warehouseId, sourceSha256, signature,
            declaredSnapshotAt, decisions, totals, orderedIssues, counts);
    }

    private static bool HasStockPayload(OpeningExportRow row) =>
        row.SkuName is not null || row.SourceSkuGuid is not null
        || row.UnitOfMeasure is not null || row.SourceQuantity is not null
        || row.SourceDate is not null;

    private static bool MatchesBaseUnit(string sourceUnit, OpeningStockSku sku)
    {
        var value = sourceUnit.Trim();
        return Matches(sku.BaseUnitDescription)
            || Matches(sku.BaseUnitName)
            || Matches(sku.BaseUnitAbbreviation);

        bool Matches(string? label) => !string.IsNullOrWhiteSpace(label)
            && string.Equals(value, label.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static string Sign(
        Guid warehouseId, string sourceHash, DateTimeOffset snapshotAt,
        OpeningStockImportState state, IReadOnlyList<OpeningStockRowPlan> rows,
        OpeningStockCounts counts)
    {
        var data = JsonSerializer.SerializeToUtf8Bytes(new
        {
            Version = 1, warehouseId, sourceHash, snapshotAt,
            state.WarehouseExists, state.WarehouseDeleted, state.Import,
            state.HasOperationalStock, state.HasOperationalWork,
            Locations = state.Locations.OrderBy(x => x.Address, StringComparer.Ordinal)
                .ThenBy(x => x.Id).ToArray(),
            Skus = state.Skus.OrderBy(x => x.Id).ToArray(),
            Rows = rows, counts
        });
        return Convert.ToHexString(SHA256.HashData(data));
    }
}

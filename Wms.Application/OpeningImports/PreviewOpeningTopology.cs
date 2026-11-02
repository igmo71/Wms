using System.Security.Cryptography;
using System.Text.Json;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Application.OpeningImports;

public sealed record OpeningTopologyImportState(
    bool WarehouseExists,
    bool WarehouseDeleted,
    OpeningTopologyExistingImport? Import,
    IReadOnlyList<OpeningTopologyExistingZone> Zones,
    IReadOnlyList<OpeningTopologyExistingLocation> Locations);

public sealed record OpeningTopologyExistingImport(
    Guid Id, string SourceSha256, WarehouseOpeningImportStatus Status, long Revision);

public sealed record OpeningTopologyExistingZone(
    Guid Id, string Code, string Name, ZoneType Type, bool DeletionMark);

public sealed record OpeningTopologyExistingLocation(
    Guid Id, string Code, string Name, Guid? ParentId, Guid? ZoneId,
    bool IsFolder, bool AllowsMultipleSkus, bool DeletionMark, long OperationalRevision);

public interface IOpeningTopologyStateStore
{
    Task<OpeningTopologyImportState> LoadAsync(Guid warehouseId, CancellationToken ct);
}

public sealed record OpeningTopologyZonePlan(string Code, string Name, ZoneType Type);

public sealed record OpeningTopologyFolderPlan(
    string Code, string Name, string? ParentCode, int Depth);

public sealed record OpeningTopologyCellPlan(
    string Address, string Name, string Area, string CellGroup, string ParentCode,
    string ZoneCode, ZoneType Purpose, bool AllowsMultipleSkus,
    OpeningImportMixingPolicy MixingPolicy, bool HasStockRows, bool Exists);

public sealed record OpeningTopologyIssue(
    int? RowNumber, string? Address, string Message, bool BlocksConfirmation);

public sealed record OpeningTopologyCounts(
    int SourceRows, int AcceptedRows, int CorrectedRows, int ExcludedRows,
    int BlockingRows, int StockRows, int EmptyCells, int DistinctCells,
    int RepeatedAddressRows, int ExistingCells, int NewCells,
    int ExistingFolders, int NewFolders, int ExistingZones, int NewZones,
    int BlockingIssues, int InformationalIssues);

public sealed record OpeningTopologyPage<T>(
    IReadOnlyList<T> Items, int Total, int Page, int PageSize);

public sealed record OpeningTopologyPreview(
    Guid WarehouseId, string SourceSha256, string Signature,
    bool CanConfirm, OpeningTopologyCounts Counts,
    IReadOnlyList<OpeningTopologyZonePlan> Zones,
    OpeningTopologyPage<OpeningTopologyCellPlan> Cells,
    OpeningTopologyPage<OpeningImportRowDecision> Decisions,
    OpeningTopologyPage<OpeningTopologyIssue> Issues);

// The complete plan is kept for confirmation; the public preview exposes only pages.
public sealed record OpeningTopologyPlan(
    Guid WarehouseId, string SourceSha256, string Signature,
    IReadOnlyList<OpeningTopologyZonePlan> Zones,
    IReadOnlyList<OpeningTopologyFolderPlan> Folders,
    IReadOnlyList<OpeningTopologyCellPlan> Cells,
    IReadOnlyList<OpeningImportRowDecision> Decisions,
    IReadOnlyList<OpeningTopologyIssue> Issues,
    OpeningTopologyCounts Counts)
{
    public bool CanConfirm => Counts.BlockingIssues == 0;

    public OpeningTopologyPreview Page(int page = 1, int pageSize = 100) =>
        new(WarehouseId, SourceSha256, Signature, CanConfirm, Counts, Zones,
            Slice(Cells, page, pageSize), Slice(Decisions, page, pageSize),
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

public sealed class PreviewOpeningTopology(
    IOpeningExportReader reader, IOpeningTopologyStateStore store)
{
    public async Task<OpeningTopologyPreview> ExecuteAsync(
        Guid warehouseId, ReadOnlyMemory<byte> workbook,
        int page = 1, int pageSize = 100, CancellationToken ct = default)
    {
        var plan = await BuildPlanAsync(warehouseId, workbook, ct);
        return plan.Page(page, pageSize);
    }

    public async Task<OpeningTopologyPlan> BuildPlanAsync(
        Guid warehouseId, ReadOnlyMemory<byte> workbook, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(warehouseId, Guid.Empty);
        var source = reader.Read(workbook, ct);
        var state = await store.LoadAsync(warehouseId, ct);
        var sourceHash = Convert.ToHexString(SHA256.HashData(workbook.Span));
        return OpeningTopologyPlanBuilder.Build(warehouseId, sourceHash, source, state);
    }
}

public static class OpeningTopologyPlanBuilder
{
    private const string GatesCode = "FGATES";

    public static OpeningTopologyPlan Build(
        Guid warehouseId, string sourceSha256, OpeningExportReadResult source,
        OpeningTopologyImportState state)
    {
        var parseIssuesByRow = source.Issues.Where(x => x.RowNumber is > 1)
            .GroupBy(x => x.RowNumber!.Value)
            .ToDictionary(x => x.Key, x => x.First().Message);
        var decisions = source.Rows.Select(row =>
        {
            var decision = OpeningImportPolicy.Interpret(row);
            return parseIssuesByRow.TryGetValue(row.RowNumber, out var message)
                ? decision with
                {
                    Outcome = OpeningImportRowOutcome.Blocking,
                    Area = null, CellGroup = null, Address = null, Purpose = null,
                    Reason = $"Ошибка чтения строки: {message}"
                }
                : decision;
        }).ToArray();
        var issues = source.Issues.Select(x => new OpeningTopologyIssue(
            x.RowNumber, null,
            x.ColumnName is null ? x.Message : $"{x.ColumnName}: {x.Message}", true)).ToList();
        foreach (var decision in decisions)
        {
            if (decision.Outcome is OpeningImportRowOutcome.Blocking or OpeningImportRowOutcome.Excluded
                or OpeningImportRowOutcome.Corrected)
                issues.Add(new OpeningTopologyIssue(decision.Source.RowNumber,
                    decision.Source.Address, decision.Reason ?? "Решение по строке источника.",
                    decision.Outcome == OpeningImportRowOutcome.Blocking));
        }
        var correctedChainRows = decisions
            .Where(x => x.Outcome == OpeningImportRowOutcome.Corrected
                && x.Address == "9Z99A1").ToArray();
        if (correctedChainRows.Length > 1)
            issues.Add(new(null, "9Z99A1",
                "Согласовано исправление только одной строки с цепью в адресе 9Z99A1.", true));

        if (!state.WarehouseExists || state.WarehouseDeleted)
            issues.Add(new(null, null, "Склад не найден или отключён.", true));
        if (state.Import is not null)
            issues.Add(new(null, null,
                state.Import.SourceSha256.Equals(sourceSha256, StringComparison.OrdinalIgnoreCase)
                    ? "На складе уже зарегистрирована загрузка топологии."
                    : "На складе уже зарегистрирована загрузка из другого файла.", true));

        var zonePlans = new Dictionary<string, OpeningTopologyZonePlan>(StringComparer.Ordinal);
        var folderPlans = new Dictionary<string, OpeningTopologyFolderPlan>(StringComparer.Ordinal);
        var cellPlans = new Dictionary<string, OpeningTopologyCellPlan>(StringComparer.Ordinal);
        var firstRows = new Dictionary<string, int>(StringComparer.Ordinal);
        var reportedDuplicates = new HashSet<string>(StringComparer.Ordinal);
        var repeatedRows = 0;
        foreach (var decision in decisions)
        {
            if (decision.Outcome is not (OpeningImportRowOutcome.Accepted
                    or OpeningImportRowOutcome.Corrected))
                continue;

            var address = decision.Address!;
            var purpose = decision.Purpose!.Value;
            var zone = ZoneFor(decision.Area!, purpose);
            zonePlans.TryAdd(zone.Code, zone);
            var parentCode = AddFolders(address, decision.Area!, folderPlans);
            var hasStock = HasStockPayload(decision.Source);
            var cell = new OpeningTopologyCellPlan(
                address, StorageLocationAddress.Format(address),
                decision.Area!, decision.CellGroup!, parentCode,
                zone.Code, purpose,
                decision.MixingPolicy == OpeningImportMixingPolicy.ApprovedMultipleSkus,
                decision.MixingPolicy, hasStock, false);

            if (cellPlans.TryGetValue(address, out var previous))
            {
                repeatedRows++;
                if (reportedDuplicates.Add(address))
                    issues.Add(new(decision.Source.RowNumber, address,
                        $"Адрес встречается в нескольких строках; первая строка {firstRows[address]}.", false));
                if (previous.Area != cell.Area || previous.CellGroup != cell.CellGroup
                    || previous.ParentCode != cell.ParentCode || previous.ZoneCode != cell.ZoneCode
                    || previous.MixingPolicy != cell.MixingPolicy)
                    issues.Add(new(decision.Source.RowNumber, address,
                        $"Адрес повторён с другой структурой или назначением; первая строка {firstRows[address]}.", true));
                else if (hasStock && !previous.HasStockRows)
                    cellPlans[address] = previous with { HasStockRows = true };
            }
            else
            {
                firstRows.Add(address, decision.Source.RowNumber);
                cellPlans.Add(address, cell);
            }
        }
        if (cellPlans.Count == 0)
            issues.Add(new(null, null, "В файле нет ячеек для загрузки.", true));

        var existingZones = state.Zones.GroupBy(x => x.Code, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);
        var existingLocations = state.Locations.GroupBy(x => x.Code, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);
        var locationCodesById = state.Locations.GroupBy(x => x.Id)
            .ToDictionary(x => x.Key, x => x.First().Code);
        var zoneCodesById = state.Zones.GroupBy(x => x.Id)
            .ToDictionary(x => x.Key, x => x.First().Code);
        foreach (var duplicate in state.Zones.GroupBy(x => x.Code, StringComparer.Ordinal).Where(x => x.Count() > 1))
            issues.Add(new(null, duplicate.Key, "В WMS уже есть повторяющийся код зоны.", true));
        foreach (var duplicate in state.Locations.GroupBy(x => x.Code, StringComparer.Ordinal).Where(x => x.Count() > 1))
            issues.Add(new(null, duplicate.Key, "В WMS уже есть повторяющийся код позиции.", true));

        var zones = zonePlans.Values.OrderBy(x => x.Code, StringComparer.Ordinal).ToArray();
        foreach (var zone in zones)
        {
            if (existingZones.TryGetValue(zone.Code, out var actual)
                && (actual.DeletionMark || actual.Type != zone.Type))
                issues.Add(new(null, zone.Code,
                    "Существующая зона имеет другое назначение или отключена.", true));
        }

        var folders = folderPlans.Values.OrderBy(x => x.Depth)
            .ThenBy(x => x.Code, StringComparer.Ordinal).ToArray();
        foreach (var folder in folders)
        {
            if (existingLocations.TryGetValue(folder.Code, out var actual)
                && (actual.DeletionMark || !actual.IsFolder || actual.ZoneId is not null
                    || !ParentMatches(actual, folder.ParentCode, locationCodesById)))
                issues.Add(new(null, folder.Code,
                    "Существующая папка имеет другую структуру или отключена.", true));
        }

        var cells = cellPlans.Values.OrderBy(x => x.Address, StringComparer.Ordinal)
            .Select(cell =>
            {
                if (!existingLocations.TryGetValue(cell.Address, out var actual))
                    return cell;
                if (actual.DeletionMark || actual.IsFolder
                    || actual.AllowsMultipleSkus != cell.AllowsMultipleSkus
                    || !ParentMatches(actual, cell.ParentCode, locationCodesById)
                    || actual.ZoneId is not Guid zoneId
                    || zoneCodesById.GetValueOrDefault(zoneId) != cell.ZoneCode)
                    issues.Add(new(null, cell.Address,
                        "Существующая ячейка имеет другую структуру, назначение, настройку SKU или отключена.", true));
                return cell with { Exists = true };
            }).ToArray();

        var orderedIssues = issues.OrderBy(x => x.RowNumber ?? int.MaxValue)
            .ThenBy(x => x.Address, StringComparer.Ordinal).ToArray();
        var counts = new OpeningTopologyCounts(
            decisions.Length,
            decisions.Count(x => x.Outcome == OpeningImportRowOutcome.Accepted),
            decisions.Count(x => x.Outcome == OpeningImportRowOutcome.Corrected),
            decisions.Count(x => x.Outcome == OpeningImportRowOutcome.Excluded),
            decisions.Count(x => x.Outcome == OpeningImportRowOutcome.Blocking),
            decisions.Count(x => (x.Outcome is OpeningImportRowOutcome.Accepted
                or OpeningImportRowOutcome.Corrected) && HasStockPayload(x.Source)),
            cells.Count(x => !x.HasStockRows), cells.Length, repeatedRows,
            cells.Count(x => x.Exists), cells.Count(x => !x.Exists),
            folders.Count(x => existingLocations.ContainsKey(x.Code)),
            folders.Count(x => !existingLocations.ContainsKey(x.Code)),
            zones.Count(x => existingZones.ContainsKey(x.Code)),
            zones.Count(x => !existingZones.ContainsKey(x.Code)),
            orderedIssues.Count(x => x.BlocksConfirmation),
            orderedIssues.Count(x => !x.BlocksConfirmation));
        var signature = Sign(warehouseId, sourceSha256, state, zones, folders, cells, decisions);
        return new OpeningTopologyPlan(warehouseId, sourceSha256, signature,
            zones, folders, cells, decisions, orderedIssues, counts);
    }

    private static OpeningTopologyZonePlan ZoneFor(string area, ZoneType purpose)
    {
        var areaCode = area switch
        {
            "(1) Фронтальные стеллажи" => "1",
            "(2) Мезонин 1 этаж 1" => "2",
            "(3) Мезонин 1 этаж 2" => "3",
            _ => "G"
        };
        var (purposeCode, purposeName) = purpose switch
        {
            ZoneType.Storage => ("PICK", "Комплектация"),
            ZoneType.Reserve => ("RESERVE", "Хранение"),
            ZoneType.Receiving => ("RECEIVING", "Приёмка"),
            ZoneType.Shipping => ("SHIPPING", "Отгрузка"),
            _ => throw new InvalidOperationException("Неподдерживаемое назначение зоны.")
        };
        return new OpeningTopologyZonePlan($"{areaCode}-{purposeCode}",
            $"{area} — {purposeName}", purpose);
    }

    private static string AddFolders(
        string address, string area, Dictionary<string, OpeningTopologyFolderPlan> folders)
    {
        if (area == "Ворота")
        {
            folders.TryAdd(GatesCode, new(GatesCode, "Ворота", null, 1));
            return GatesCode;
        }

        var room = address[0];
        var rackRow = address[1];
        var section = address.Substring(2, 2);
        var level = address[5];
        var roomCode = $"FROOM{room}";
        var rowCode = $"FROW{room}{rackRow}";
        var sectionCode = $"FSECTION{room}{rackRow}{section}";
        var levelCode = $"FLEVEL{room}{rackRow}{section}L{level}";
        folders.TryAdd(roomCode, new(roomCode, $"Помещение {room}", null, 1));
        folders.TryAdd(rowCode, new(rowCode, $"Ряд {rackRow}", roomCode, 2));
        folders.TryAdd(sectionCode, new(sectionCode, $"Стойка {section}", rowCode, 3));
        folders.TryAdd(levelCode, new(levelCode, $"Уровень {level}", sectionCode, 4));
        return levelCode;
    }

    private static bool HasStockPayload(OpeningExportRow row) =>
        row.SkuName is not null || row.SourceSkuGuid is not null
        || row.UnitOfMeasure is not null || row.SourceQuantity is not null
        || row.SourceDate is not null;

    private static bool ParentMatches(
        OpeningTopologyExistingLocation location, string? expectedCode,
        IReadOnlyDictionary<Guid, string> codesById) =>
        location.ParentId is Guid parentId
            ? codesById.TryGetValue(parentId, out var actualCode)
              && actualCode == expectedCode
            : expectedCode is null;

    private static string Sign(
        Guid warehouseId, string sourceHash, OpeningTopologyImportState state,
        IReadOnlyList<OpeningTopologyZonePlan> zones,
        IReadOnlyList<OpeningTopologyFolderPlan> folders,
        IReadOnlyList<OpeningTopologyCellPlan> cells,
        IReadOnlyList<OpeningImportRowDecision> decisions)
    {
        var data = JsonSerializer.SerializeToUtf8Bytes(new
        {
            Version = 1, warehouseId, sourceHash,
            state.WarehouseExists, state.WarehouseDeleted, state.Import,
            Zones = state.Zones.OrderBy(x => x.Code, StringComparer.Ordinal).ThenBy(x => x.Id).ToArray(),
            Locations = state.Locations.OrderBy(x => x.Code, StringComparer.Ordinal).ThenBy(x => x.Id).ToArray(),
            ProposedZones = zones, ProposedFolders = folders,
            ProposedCells = cells.Select(x => new
            {
                x.Address, x.Name, x.Area, x.CellGroup, x.ParentCode, x.ZoneCode,
                x.Purpose, x.AllowsMultipleSkus, x.MixingPolicy, x.HasStockRows
            }).ToArray(),
            RowOutcomes = decisions.Select(x => new
            {
                x.Source.RowNumber, x.Outcome, x.Area, x.CellGroup,
                x.Address, x.Purpose, x.MixingPolicy, x.Reason
            }).ToArray()
        });
        return Convert.ToHexString(SHA256.HashData(data));
    }
}

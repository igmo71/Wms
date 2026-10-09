using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using ExcelDataReader;
using Microsoft.EntityFrameworkCore;
using Wms.Application.Persistence;
using Wms.Common;
using Wms.Data;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Application.StorageLocations;

public sealed class StorageMapImportService(IDbContextFactory<ApplicationDbContext> dbContextFactory)
{
    public const int MaximumFileCount = 40;
    public const int MaximumFileBytes = 2_000_000;
    private const int MaximumEntriesPerFile = 50_000;

    static StorageMapImportService() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public static OperationResult<StorageMapIdentity> Identify(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)
            || !string.Equals(Path.GetExtension(fileName), ".xls", StringComparison.OrdinalIgnoreCase))
            return OperationError.Invalid("Выберите файл карты в формате XLS.");

        var name = Path.GetFileNameWithoutExtension(fileName);
        var area = Regex.Match(name, @"^\((?<area>[1-5])\)");
        if (area.Success)
        {
            var tier = Regex.Match(name, @"Ярус\s*-?\s*(?<tier>\d+)$", RegexOptions.IgnoreCase);
            if (name.Contains("Ярус", StringComparison.OrdinalIgnoreCase) && !tier.Success)
                return OperationError.Invalid($"Не удалось определить ярус файла «{fileName}».");
            return new StorageMapIdentity(
                area.Groups["area"].Value,
                tier.Success ? StorageMapKind.Tier : StorageMapKind.Overview,
                tier.Success ? int.Parse(tier.Groups["tier"].Value, CultureInfo.InvariantCulture) : null);
        }

        if (name.Equals("Ворота", StringComparison.OrdinalIgnoreCase))
            return new StorageMapIdentity("gates", StorageMapKind.Individual, null);
        if (name.Equals("Улица", StringComparison.OrdinalIgnoreCase))
            return new StorageMapIdentity("outside", StorageMapKind.Individual, null);

        return OperationError.Invalid($"Неизвестная карта «{fileName}». Название должно соответствовать выгрузке 1С.");
    }

    public async Task<OperationResult<StorageMapImportPreview>> PreviewAsync(
        Guid warehouseId,
        IReadOnlyList<StorageMapImportFile> files,
        CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        var plan = await BuildPlanAsync(dbContext, warehouseId, files, ct);
        if (!plan.IsSuccess)
            return plan.Error!;
        return plan.Value!.Preview;
    }

    public async Task<OperationResult<StorageMapImportResult>> ImportAsync(
        Guid warehouseId,
        IReadOnlyList<StorageMapImportFile> files,
        string expectedSignature,
        bool acknowledgeWarnings,
        CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        var planResult = await BuildPlanAsync(dbContext, warehouseId, files, ct);
        if (!planResult.IsSuccess)
            return planResult.Error!;

        var plan = planResult.Value!;
        if (plan.Preview.Signature != expectedSignature)
            return OperationError.Conflict("Файлы или сопоставление зон изменились. Обновите предпросмотр.");
        if (plan.Preview.HasBlockingIssues)
            return OperationError.Invalid("Исправьте ошибки предпросмотра перед загрузкой.");
        if (plan.Preview.HasWarnings && !acknowledgeWarnings)
            return OperationError.Invalid("Подтвердите просмотр расхождений карт.");

        var ids = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var folder in plan.Folders)
        {
            if (plan.Existing.TryGetValue(folder.Code, out var existing))
            {
                ids[folder.Code] = existing.Id;
                continue;
            }

            var details = StorageLocationDetails.Create(
                folder.Name, true, LocationDimensions.Empty, LocationCoordinates.Empty, null);
            if (!details.IsSuccess)
                return details.Error!;
            var location = StorageLocation.Create(
                Guid.NewGuid(), warehouseId, null,
                folder.ParentCode is null ? null : ids[folder.ParentCode],
                folder.Code, details.Value!);
            if (!location.IsSuccess)
                return location.Error!;
            ids[folder.Code] = location.Value!.Id;
            dbContext.StorageLocations.Add(location.Value!);
        }

        foreach (var entry in plan.Locations)
        {
            if (plan.Existing.ContainsKey(entry.Code))
                continue;

            var details = StorageLocationDetails.Create(
                StorageLocationAddress.Format(entry.Code), false,
                LocationDimensions.Empty, LocationCoordinates.Empty, null);
            if (!details.IsSuccess)
                return details.Error!;
            var location = StorageLocation.Create(
                Guid.NewGuid(), warehouseId, entry.ZoneId,
                ids[LevelCode(entry)], entry.Code, details.Value!);
            if (!location.IsSuccess)
                return location.Error!;
            dbContext.StorageLocations.Add(location.Value!);
        }

        var saved = await ApplicationPersistence.SaveChangesAsync(dbContext, ct);
        if (!saved.IsSuccess)
            return saved.Error!;

        return new StorageMapImportResult(
            plan.Preview.NewLocationCount,
            plan.Preview.NewFolderCount,
            plan.Preview.ExistingLocationCount);
    }

    private static async Task<OperationResult<ImportPlan>> BuildPlanAsync(
        ApplicationDbContext dbContext,
        Guid warehouseId,
        IReadOnlyList<StorageMapImportFile> files,
        CancellationToken ct)
    {
        if (warehouseId == Guid.Empty || !await dbContext.Warehouses
            .AnyAsync(x => x.Id == warehouseId && !x.DeletionMark, ct))
            return OperationError.Invalid("Выберите активный склад.");
        if (files.Count is < 1 or > MaximumFileCount)
            return OperationError.Invalid($"Выберите от 1 до {MaximumFileCount} карт.");
        if (!files.Any(x => x.Include))
            return OperationError.Invalid("Отметьте хотя бы одну карту для загрузки.");

        var issues = new List<StorageMapIssue>();
        var summaries = new List<StorageMapFileSummary>();
        var detailed = new List<ParsedAddress>();
        var overview = new Dictionary<(string Area, string Prefix), List<ParsedAddress>>();
        var zoneIds = files.Where(x => x.Include && x.ZoneId.HasValue)
            .Select(x => x.ZoneId!.Value).Distinct().ToArray();
        var zones = await dbContext.Zones.AsNoTracking()
            .Where(x => zoneIds.Contains(x.Id) && x.WarehouseId == warehouseId && !x.DeletionMark)
            .ToDictionaryAsync(x => x.Id, ct);
        var mappedAreas = new Dictionary<string, Guid>();

        foreach (var file in files.Where(x => x.Include))
        {
            var identity = Identify(file.Name);
            if (!identity.IsSuccess || identity.Value != file.Identity)
            {
                issues.Add(new(file.Name, null, null, null,
                    identity.Error?.Message ?? "Тип карты изменился. Загрузите файл повторно.", true));
                continue;
            }
            if (file.Content.Length is 0 or > MaximumFileBytes)
            {
                issues.Add(new(file.Name, null, null, null, "Размер XLS-файла недопустим.", true));
                continue;
            }

            var entries = ReadFile(file, issues);
            summaries.Add(new(file.Name, file.Identity.Area, file.Identity.Kind,
                entries.Count, entries.Select(x => x.Code).Distinct(StringComparer.Ordinal).Count()));
            if (entries.Count == 0)
                continue;

            if (file.Identity.Area == "gates")
            {
                issues.Add(new(file.Name, null, null, null,
                    "Для адресов ворот сначала нужно определить функциональную зону каждого места.", true));
                continue;
            }

            if (file.Identity.Kind != StorageMapKind.Overview)
            {
                if (file.ZoneId is not Guid zoneId || !zones.TryGetValue(zoneId, out var zone))
                {
                    issues.Add(new(file.Name, null, null, null,
                        "Выберите активную зону этого склада для карты.", true));
                    continue;
                }
                if (file.Identity.Area is "1" or "2" or "3" && zone.Type != ZoneType.Storage)
                    issues.Add(new(file.Name, null, null, null,
                        "Для стеллажей и мезонина выберите зону хранения.", true));
                if (mappedAreas.TryGetValue(file.Identity.Area, out var mappedZoneId) && mappedZoneId != zoneId)
                    issues.Add(new(file.Name, null, null, null,
                        "Карты одного участка сопоставлены с разными зонами.", true));
                mappedAreas[file.Identity.Area] = zoneId;
                detailed.AddRange(entries.Select(x => x with { ZoneId = zoneId }));
            }
            else
            {
                foreach (var entry in entries)
                {
                    var key = (file.Identity.Area, entry.Prefix);
                    if (!overview.TryGetValue(key, out var values))
                        overview[key] = values = [];
                    values.Add(entry);
                }
            }
        }

        var unique = new Dictionary<string, ParsedAddress>(StringComparer.Ordinal);
        foreach (var entry in detailed)
        {
            if (entry.Code == "4A10A1")
            {
                issues.Add(new(entry.File, entry.Row, entry.Column, entry.Code,
                    "Адрес помещения 4 отложен по согласованному решению.", false));
                continue;
            }
            if (unique.TryGetValue(entry.Code, out var first))
            {
                issues.Add(new(entry.File, entry.Row, entry.Column, entry.Code,
                    first.Area == entry.Area && first.ZoneId == entry.ZoneId
                        ? "Повторный адрес в поярусных картах; будет создан один раз."
                        : "Один адрес относится к разным участкам или зонам.",
                    first.Area != entry.Area || first.ZoneId != entry.ZoneId));
                continue;
            }
            unique.Add(entry.Code, entry);
        }
        if (unique.Count == 0)
            issues.Add(new("Карты 1С", null, null, null,
                "В выбранных картах нет конкретных адресов для загрузки.", true));

        var actualGroups = unique.Values
            .GroupBy(x => (x.Area, x.Prefix))
            .ToDictionary(x => x.Key, x => x.Max(y => y.Level));
        var overviewAreas = overview.Keys.Select(x => x.Area).ToHashSet(StringComparer.Ordinal);
        var overviewLevels = overview.ToDictionary(x => x.Key, x => x.Value.Max(y => y.Level));
        foreach (var group in overview)
        {
            var levels = group.Value.Select(x => x.Level).Distinct().ToArray();
            if (levels.Length > 1)
                issues.Add(new(group.Value[0].File, group.Value[0].Row, group.Value[0].Column,
                    group.Key.Prefix, "В сводной карте для группы указаны разные верхние уровни.", false));
            if (!actualGroups.TryGetValue(group.Key, out var actualMaximum))
                issues.Add(new(group.Value[0].File, group.Value[0].Row, group.Value[0].Column,
                    group.Key.Prefix, "Группа есть в сводной карте, но отсутствует в поярусных.", false));
            else if (actualMaximum != levels.Max())
                issues.Add(new(group.Value[0].File, group.Value[0].Row, group.Value[0].Column,
                    group.Key.Prefix, "Верхний уровень сводной и поярусных карт отличается.", false));
        }
        foreach (var entry in unique.Values)
        {
            if (overviewAreas.Contains(entry.Area) && !overviewLevels.ContainsKey((entry.Area, entry.Prefix)))
                issues.Add(new(entry.File, entry.Row, entry.Column, entry.Code,
                    "Адрес есть только в поярусной карте.", false));
            else if (overviewLevels.TryGetValue((entry.Area, entry.Prefix), out var maximum)
                     && entry.Level > maximum)
                issues.Add(new(entry.File, entry.Row, entry.Column, entry.Code,
                    "Уровень адреса выше указанного в сводной карте.", false));
        }

        var folders = BuildFolders(unique.Values);
        var existingLocations = await dbContext.StorageLocations.AsNoTracking()
            .Where(x => x.WarehouseId == warehouseId)
            .Select(x => new ExistingLocation(x.Id, x.Code, x.ParentId, x.ZoneId, x.IsFolder, x.DeletionMark))
            .ToListAsync(ct);
        var existing = existingLocations.ToDictionary(x => x.Code, StringComparer.Ordinal);
        var existingCodesById = existingLocations.ToDictionary(x => x.Id, x => x.Code);
        foreach (var folder in folders)
            CheckExisting(folder.Code, folder.ParentCode, null, true, existing, existingCodesById, issues);
        foreach (var entry in unique.Values)
            CheckExisting(entry.Code, LevelCode(entry), entry.ZoneId, false, existing, existingCodesById, issues);

        var locations = unique.Values.OrderBy(x => x.Code, StringComparer.Ordinal).ToArray();
        var preview = new StorageMapImportPreview(
            Signature(warehouseId, files), summaries,
            locations.Select(x => new StorageMapLocationPreview(
                x.Code, StorageLocationAddress.Format(x.Code), x.Area,
                x.ZoneId is Guid zoneId && zones.TryGetValue(zoneId, out var zone) ? zone.Name : "—",
                existing.ContainsKey(x.Code))).ToArray(),
            issues,
            folders.Count,
            folders.Count(x => !existing.ContainsKey(x.Code)));
        return new ImportPlan(preview, folders, locations, existing);
    }

    private static List<ParsedAddress> ReadFile(StorageMapImportFile file, List<StorageMapIssue> issues)
    {
        var entries = new List<ParsedAddress>();
        try
        {
            using var stream = new MemoryStream(file.Content, writable: false);
            using var reader = ExcelReaderFactory.CreateBinaryReader(stream);
            do
            {
                var row = 0;
                while (reader.Read())
                {
                    row++;
                    for (var column = 0; column < reader.FieldCount; column++)
                    {
                        var raw = reader.GetValue(column);
                        if (raw is null || raw is string { Length: 0 })
                            continue;
                        if (entries.Count >= MaximumEntriesPerFile)
                        {
                            issues.Add(new(file.Name, row, column + 1, null,
                                "В файле слишком много записей.", true));
                            return entries;
                        }
                        if (raw is not string text || !TryParseAddress(text, out var address))
                        {
                            issues.Add(new(file.Name, row, column + 1, raw.ToString(),
                                "Запись не похожа на адрес ячейки.", true));
                            continue;
                        }
                        if (file.Identity.Tier is int tier && address.Level != tier)
                            issues.Add(new(file.Name, row, column + 1, address.Code,
                                $"Адрес не соответствует ярусу {tier} в имени файла.", true));
                        entries.Add(address with
                        {
                            File = file.Name,
                            Area = file.Identity.Area,
                            Row = row,
                            Column = column + 1
                        });
                    }
                }
            } while (reader.NextResult());
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            issues.Add(new(file.Name, null, null, null,
                "Не удалось прочитать XLS-файл. Проверьте выгрузку 1С.", true));
        }
        return entries;
    }

    private static bool TryParseAddress(string text, out ParsedAddress address)
    {
        address = null!;
        var segments = text.Trim().ToUpperInvariant().Split('\'');
        if (segments.Length != 5
            || segments[0].Length != 1 || !char.IsAsciiDigit(segments[0][0])
            || segments[1].Length != 1 || segments[1][0] is < 'A' or > 'Z'
            || segments[2].Length != 2 || !segments[2].All(char.IsAsciiDigit)
            || segments[3].Length != 1 || segments[3][0] is < 'A' or > 'Z'
            || segments[4].Length != 1 || !char.IsAsciiDigit(segments[4][0]))
            return false;

        var code = string.Concat(segments);
        address = new ParsedAddress(code, segments[0], segments[1], segments[2],
            segments[3], int.Parse(segments[4], CultureInfo.InvariantCulture),
            string.Empty, string.Empty, 0, 0, null);
        return true;
    }

    private static List<FolderPlan> BuildFolders(IEnumerable<ParsedAddress> entries)
    {
        var folders = new Dictionary<string, FolderPlan>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            var room = $"FROOM{entry.Room}";
            var row = $"FROW{entry.Room}{entry.RackRow}";
            var section = $"FSECTION{entry.Room}{entry.RackRow}{entry.Section}";
            var level = LevelCode(entry);
            folders.TryAdd(room, new(room, $"Помещение {entry.Room}", null, 1));
            folders.TryAdd(row, new(row, $"Ряд {entry.RackRow}", room, 2));
            folders.TryAdd(section, new(section, $"Стойка {entry.Section}", row, 3));
            folders.TryAdd(level, new(level, $"Уровень {entry.Level}", section, 4));
        }
        return folders.Values.OrderBy(x => x.Depth).ThenBy(x => x.Code, StringComparer.Ordinal).ToList();
    }

    private static string LevelCode(ParsedAddress entry) =>
        $"FLEVEL{entry.Room}{entry.RackRow}{entry.Section}L{entry.Level}";

    private static void CheckExisting(
        string code,
        string? parentCode,
        Guid? zoneId,
        bool isFolder,
        IReadOnlyDictionary<string, ExistingLocation> existing,
        IReadOnlyDictionary<Guid, string> existingCodesById,
        List<StorageMapIssue> issues)
    {
        if (!existing.TryGetValue(code, out var location))
            return;
        var actualParentCode = location.ParentId is Guid parentId
            ? existingCodesById.GetValueOrDefault(parentId)
            : null;
        if (location.DeletionMark || location.IsFolder != isFolder
            || location.ZoneId != zoneId || actualParentCode != parentCode)
            issues.Add(new("WMS", null, null, code,
                "Адрес уже занят другой позицией или имеет другую структуру.", true));
    }

    private static string Signature(Guid warehouseId, IReadOnlyList<StorageMapImportFile> files)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(warehouseId.ToByteArray());
        foreach (var file in files)
        {
            var header = $"{file.Name.Length}:{file.Name}|{file.Identity.Area}|{file.Identity.Kind}|{file.Identity.Tier}|{file.ZoneId}|{file.Include}|{file.Content.Length}:";
            hash.AppendData(Encoding.UTF8.GetBytes(header));
            hash.AppendData(file.Content);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private sealed record ParsedAddress(
        string Code, string Room, string RackRow, string Section, string Cell, int Level,
        string File, string Area, int Row, int Column, Guid? ZoneId)
    {
        public string Prefix => Code[..5];
    }

    private sealed record FolderPlan(string Code, string Name, string? ParentCode, int Depth);
    private sealed record ExistingLocation(Guid Id, string Code, Guid? ParentId, Guid? ZoneId, bool IsFolder, bool DeletionMark);
    private sealed record ImportPlan(
        StorageMapImportPreview Preview,
        IReadOnlyList<FolderPlan> Folders,
        IReadOnlyList<ParsedAddress> Locations,
        IReadOnlyDictionary<string, ExistingLocation> Existing);
}

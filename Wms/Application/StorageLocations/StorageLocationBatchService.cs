using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Wms.Application.Persistence;
using Wms.Common;
using Wms.Data;
using Wms.Domain;

namespace Wms.Application.StorageLocations;

public sealed record StorageLocationRange(
    Guid WarehouseId,
    Guid ZoneId,
    Guid ParentId,
    int RoomFrom,
    int RoomTo,
    string RowFrom,
    string RowTo,
    int SectionFrom,
    int SectionTo,
    string CellFrom,
    string CellTo,
    int LevelFrom,
    int LevelTo);

public sealed record StorageLocationBatchPreview(
    string Signature,
    IReadOnlyList<string> Codes,
    IReadOnlyList<string> Conflicts)
{
    public int Count => Codes.Count;
    public bool CanCreate => Codes.Count > 0 && Conflicts.Count == 0;
}

public sealed class StorageLocationBatchService(IDbContextFactory<ApplicationDbContext> dbContextFactory)
{
    public const int MaximumBatchSize = 1000;

    public async Task<OperationResult<StorageLocationBatchPreview>> PreviewAsync(
        StorageLocationRange range,
        CancellationToken ct = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        return await BuildPreviewAsync(db, range, ct);
    }

    public async Task<OperationResult<int>> CreateAsync(
        StorageLocationRange range,
        string expectedSignature,
        CancellationToken ct = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        var previewResult = await BuildPreviewAsync(db, range, ct);
        if (!previewResult.IsSuccess)
            return previewResult.Error!;
        var preview = previewResult.Value!;
        if (preview.Signature != expectedSignature)
            return OperationError.Conflict("Параметры изменились. Повторите предпросмотр.");
        if (!preview.CanCreate)
            return OperationError.Conflict("Некоторые адреса уже существуют. Измените диапазоны.");

        var locations = new List<StorageLocation>(preview.Count);
        foreach (var code in preview.Codes)
        {
            var detailsResult = StorageLocationDetails.Create(
                StorageLocationAddress.Format(code), false,
                LocationDimensions.Empty, LocationCoordinates.Empty, null);
            if (!detailsResult.IsSuccess)
                return detailsResult.Error!;
            var locationResult = StorageLocation.Create(
                Guid.NewGuid(), range.WarehouseId, range.ZoneId, range.ParentId,
                code, detailsResult.Value!);
            if (!locationResult.IsSuccess)
                return locationResult.Error!;
            locations.Add(locationResult.Value!);
        }

        db.StorageLocations.AddRange(locations);
        var saved = await ApplicationPersistence.SaveChangesAsync(db, ct);
        if (!saved.IsSuccess)
            return saved.Error!;
        return locations.Count;
    }

    private static async Task<OperationResult<StorageLocationBatchPreview>> BuildPreviewAsync(
        ApplicationDbContext db,
        StorageLocationRange range,
        CancellationToken ct)
    {
        if (range.WarehouseId == Guid.Empty || range.ZoneId == Guid.Empty || range.ParentId == Guid.Empty)
            return OperationError.Invalid("Выберите склад, папку и функциональную зону.");

        var parent = await db.StorageLocations.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == range.ParentId, ct);
        if (parent is null || parent.WarehouseId != range.WarehouseId
            || parent.DeletionMark || !parent.IsFolder)
            return OperationError.Invalid("Выберите активную папку этого склада.");
        if (!await db.Warehouses.AnyAsync(x => x.Id == range.WarehouseId && !x.DeletionMark, ct))
            return OperationError.Invalid("Склад недоступен.");
        if (!await db.Zones.AnyAsync(x => x.Id == range.ZoneId
            && x.WarehouseId == range.WarehouseId && !x.DeletionMark, ct))
            return OperationError.Invalid("Выберите активную зону этого склада.");

        var rowFrom = Letter(range.RowFrom);
        var rowTo = Letter(range.RowTo);
        var cellFrom = Letter(range.CellFrom);
        var cellTo = Letter(range.CellTo);
        if (range.RoomFrom is < 0 or > 9 || range.RoomTo < range.RoomFrom || range.RoomTo > 9
            || range.SectionFrom is < 0 or > 99 || range.SectionTo < range.SectionFrom || range.SectionTo > 99
            || range.LevelFrom is < 0 or > 9 || range.LevelTo < range.LevelFrom || range.LevelTo > 9
            || rowFrom is null || rowTo is null || rowFrom > rowTo
            || cellFrom is null || cellTo is null || cellFrom > cellTo)
            return OperationError.Invalid("Проверьте диапазоны: помещение и уровень 0–9, стойка 00–99, буквы ряда и ячейки A–Z.");

        var count = (long)(range.RoomTo - range.RoomFrom + 1)
            * (rowTo.Value - rowFrom.Value + 1)
            * (range.SectionTo - range.SectionFrom + 1)
            * (cellTo.Value - cellFrom.Value + 1)
            * (range.LevelTo - range.LevelFrom + 1);
        if (count > MaximumBatchSize)
            return OperationError.Invalid($"За один раз можно создать не более {MaximumBatchSize} ячеек.");

        var codes = new List<string>((int)count);
        for (var room = range.RoomFrom; room <= range.RoomTo; room++)
        for (var row = rowFrom.Value; row <= rowTo.Value; row++)
        for (var section = range.SectionFrom; section <= range.SectionTo; section++)
        for (var cell = cellFrom.Value; cell <= cellTo.Value; cell++)
        for (var level = range.LevelFrom; level <= range.LevelTo; level++)
        {
            if (!MatchesImportedParent(parent.Code, room, row, section, level))
                return OperationError.Invalid("Диапазон не соответствует выбранной папке склада. Выберите подходящую папку или скорректируйте сегменты.");
            codes.Add($"{room}{row}{section.ToString("00", CultureInfo.InvariantCulture)}{cell}{level}");
        }

        var existing = await db.StorageLocations.AsNoTracking()
            .Where(x => x.WarehouseId == range.WarehouseId && codes.Contains(x.Code))
            .Select(x => x.Code)
            .ToListAsync(ct);
        var conflicts = existing.OrderBy(x => x, StringComparer.Ordinal).ToArray();
        return new StorageLocationBatchPreview(Signature(range, codes), codes, conflicts);
    }

    private static char? Letter(string? value)
    {
        var text = value?.Trim().ToUpperInvariant();
        return text is { Length: 1 } && text[0] is >= 'A' and <= 'Z' ? text[0] : null;
    }

    private static bool MatchesImportedParent(string parentCode, int room, char row, int section, int level)
    {
        var roomCode = $"FROOM{room}";
        var rowCode = $"FROW{room}{row}";
        var sectionCode = $"FSECTION{room}{row}{section.ToString("00", CultureInfo.InvariantCulture)}";
        var levelCode = $"FLEVEL{room}{row}{section.ToString("00", CultureInfo.InvariantCulture)}L{level}";
        if (parentCode.StartsWith("FROOM", StringComparison.Ordinal))
            return parentCode == roomCode;
        if (parentCode.StartsWith("FROW", StringComparison.Ordinal))
            return parentCode == rowCode;
        if (parentCode.StartsWith("FSECTION", StringComparison.Ordinal))
            return parentCode == sectionCode;
        if (parentCode.StartsWith("FLEVEL", StringComparison.Ordinal))
            return parentCode == levelCode;
        return true;
    }

    private static string Signature(StorageLocationRange range, IReadOnlyList<string> codes)
    {
        var source = $"{range.WarehouseId:N}|{range.ZoneId:N}|{range.ParentId:N}|{string.Join(',', codes)}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)));
    }
}

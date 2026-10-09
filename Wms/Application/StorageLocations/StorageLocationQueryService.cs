using Microsoft.EntityFrameworkCore;
using Wms.Common;
using Wms.Data;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Application.StorageLocations;

public class StorageLocationQueryService(IDbContextFactory<ApplicationDbContext> dbContextFactory)
{
    public async Task<OperationResult<IReadOnlyList<string>>> GetForPrintAsync(
        Guid id,
        bool batch,
        CancellationToken ct = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        var selected = await db.StorageLocations.AsNoTracking()
            .Include(x => x.Warehouse)
            .Include(x => x.Zone)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (selected is null)
            return OperationError.NotFound("Складская позиция не найдена.");
        if (selected.DeletionMark || selected.Warehouse?.DeletionMark != false)
            return OperationError.Invalid("Складская позиция недоступна для печати.");

        if (!batch)
        {
            if (selected.IsFolder || selected.Zone?.DeletionMark != false
                || StorageLocationAddress.Format(selected.Code) == selected.Code)
                return OperationError.Invalid("Выберите активную ячейку с адресом из пяти сегментов.");
            return OperationResult<IReadOnlyList<string>>.Success(new[] { selected.Code });
        }

        if (!selected.IsFolder)
            return OperationError.Invalid("Для печати набора выберите папку.");
        var codes = await db.StorageLocations.AsNoTracking()
            .Where(x => x.ParentId == id && !x.DeletionMark && !x.IsFolder
                && x.Zone != null && !x.Zone.DeletionMark)
            .OrderBy(x => x.Code)
            .Select(x => x.Code)
            .ToListAsync(ct);
        if (codes.Count == 0)
            return OperationError.Invalid("В папке нет активных ячеек для печати.");
        if (codes.Count > 120)
            return OperationError.Invalid("Для печати выберите папку не более чем со 120 ячейками.");
        if (codes.Any(x => StorageLocationAddress.Format(x) == x))
            return OperationError.Invalid("В папке есть ячейки без пятисегментного адреса. Выберите отдельные ячейки.");
        return OperationResult<IReadOnlyList<string>>.Success(codes);
    }

    public async Task<OperationResult<StorageLocation>> ResolveBarcodeAsync(
        string? barcode,
        Guid? expectedWarehouseId,
        ZoneType? expectedZoneType,
        CancellationToken ct = default)
    {
        OperationResult<string> address = StorageLocationAddress.Normalize(barcode);
        if (!address.IsSuccess)
            return OperationError.Invalid("Штрихкод ячейки не распознан.");

        var code = address.Value!;
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        var locations = await dbContext.StorageLocations
            .AsNoTracking()
            .Include(x => x.Warehouse)
            .Include(x => x.Zone)
            .Include(x => x.ActiveLock)
            .Where(x => x.Code == code
                && (expectedWarehouseId == null || x.WarehouseId == expectedWarehouseId))
            .Take(2)
            .ToListAsync(ct);

        if (locations.Count > 1)
            return OperationError.Conflict("Этот адрес есть на нескольких складах. Выберите склад.");

        var location = locations.SingleOrDefault();

        if (location is null)
        {
            return OperationError.NotFound("Ячейка не найдена.");
        }

        if (location.DeletionMark
            || location.Warehouse is null
            || location.Warehouse.DeletionMark
            || location.Zone is null
            || location.Zone.DeletionMark)
        {
            return OperationError.Invalid("Ячейка недоступна.");
        }

        if (location.IsFolder)
        {
            return OperationError.Invalid("Отсканированная позиция является группой, а не ячейкой.");
        }

        if (expectedWarehouseId is Guid warehouseId
            && location.WarehouseId != warehouseId)
        {
            return OperationError.Invalid("Ячейка принадлежит другому складу.");
        }

        if (expectedZoneType is ZoneType zoneType
            && location.Zone.Type != zoneType)
        {
            return OperationError.Invalid("Ячейка не подходит для текущей операции.");
        }

        if (location.ActiveLock is not null)
        {
            return StorageLocationAvailability.LockedConflict(location);
        }

        return location;
    }

    public async Task<IReadOnlyList<StorageLocation>> GetTreeAsync(
        Guid warehouseId,
        bool includeDeleted,
        CancellationToken ct = default)
    {
        await using ApplicationDbContext dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        IQueryable<StorageLocation> locations = dbContext.StorageLocations
            .AsNoTracking()
            .Include(x => x.Warehouse)
            .Include(x => x.Zone)
            .Include(x => x.ActiveLock)
            .Where(x => x.WarehouseId == warehouseId);

        if (!includeDeleted)
        {
            locations = locations.Where(x => !x.DeletionMark);
        }

        return await locations.OrderBy(x => x.Code).ToListAsync(ct);
    }

    public async Task<ListResult<StorageLocation>> ListAsync(
        StorageLocationListQuery query,
        CancellationToken ct = default)
    {
        await using ApplicationDbContext dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        IQueryable<StorageLocation> locations = dbContext.StorageLocations
            .AsNoTracking()
            .Include(x => x.Warehouse)
            .Include(x => x.Zone)
            .Include(x => x.ActiveLock);

        if (query.ExcludeDeleted)
        {
            locations = locations.Where(x => !x.DeletionMark);
        }

        if (query.ExcludeFolders)
        {
            locations = locations.Where(x => !x.IsFolder);
        }

        if (query.ExcludeLocked)
        {
            locations = locations.Where(x => x.ActiveLock == null);
        }

        if (query.WarehouseId is Guid warehouseId)
        {
            locations = locations.Where(x => x.WarehouseId == warehouseId);
        }

        if (query.ZoneId is Guid zoneId)
        {
            locations = locations.Where(x => x.ZoneId == zoneId);
        }

        if (query.ZoneType is Domain.Enums.ZoneType zoneType)
        {
            locations = locations.Where(x => x.Zone!.Type == zoneType);
        }

        if (!string.IsNullOrWhiteSpace(query.SearchString))
        {
            locations = locations.Where(x => x.Name!.Contains(query.SearchString)
                || x.Code!.Contains(query.SearchString));
        }

        var totalItems = await locations.CountAsync(ct);
        locations = query.SortBy switch
        {
            "Name" => query.SortDescending
                ? locations.OrderByDescending(x => x.Name)
                : locations.OrderBy(x => x.Name),
            "Code" => query.SortDescending
                ? locations.OrderByDescending(x => x.Code)
                : locations.OrderBy(x => x.Code),
            _ => locations.OrderBy(x => x.Code)
        };

        return new ListResult<StorageLocation>
        {
            Items = await locations.Skip(query.Skip).Take(query.Take).ToListAsync(ct),
            TotalItems = totalItems
        };
    }
}

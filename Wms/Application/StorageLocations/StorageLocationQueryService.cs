using Microsoft.EntityFrameworkCore;
using Wms.Common;
using Wms.Data;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Application.StorageLocations;

public class StorageLocationQueryService(IDbContextFactory<ApplicationDbContext> dbContextFactory)
{
    public async Task<OperationResult<StorageLocation>> ResolveBarcodeAsync(
        string? barcode,
        Guid? expectedWarehouseId,
        ZoneType? expectedZoneType,
        CancellationToken ct = default)
    {
        if (!StorageLocation.TryParseBarcode(barcode, out var locationId))
        {
            return OperationError.Invalid("Некорректный QR-код ячейки.");
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        var location = await dbContext.StorageLocations
            .AsNoTracking()
            .Include(x => x.Warehouse)
            .Include(x => x.Zone)
            .Include(x => x.ActiveLock)
            .SingleOrDefaultAsync(x => x.Id == locationId, ct);

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
        Guid zoneId,
        bool includeDeleted,
        CancellationToken ct = default)
    {
        await using ApplicationDbContext dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        IQueryable<StorageLocation> locations = dbContext.StorageLocations
            .AsNoTracking()
            .Include(x => x.Warehouse)
            .Include(x => x.Zone)
            .Include(x => x.ActiveLock)
            .Where(x => x.ZoneId == zoneId);

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

using Microsoft.EntityFrameworkCore;
using Wms.Application.StorageLocations;
using Wms.Common;
using Wms.Data;
using Wms.Data.Configurations;
using Wms.Domain;

namespace Wms.Application.Services;

public class StorageLocationService(IDbContextFactory<ApplicationDbContext> dbContextFactory)
{
    public async Task<OperationResult<StorageLocation>> CreateAsync(
        CreateStorageLocationRequest request,
        CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        var contextResult = await ValidateContextAsync(dbContext, request.WarehouseId, request.ZoneId, request.ParentId, ct);
        if (!contextResult.IsSuccess)
        {
            return contextResult.Error!;
        }

        var codeResult = BuildCode(contextResult.Value, request.Number, request.SegmentWidth);
        if (!codeResult.IsSuccess)
        {
            return codeResult.Error!;
        }

        var code = codeResult.Value!;
        if (await dbContext.StorageLocations.AnyAsync(x => x.ZoneId == request.ZoneId && x.Code == code, ct))
        {
            return OperationError.Conflict<StorageLocation>("В выбранной зоне уже есть позиция с таким кодом.");
        }

        var locationResult = StorageLocation.Create(
            Guid.NewGuid(),
            request.WarehouseId,
            request.ZoneId,
            request.ParentId,
            request.Number,
            code,
            request.Details);

        if (!locationResult.IsSuccess)
        {
            return locationResult.Error!;
        }

        var location = locationResult.Value!;
        dbContext.StorageLocations.Add(location);
        await dbContext.SaveChangesAsync(ct);
        return location;
    }

    public async Task<OperationResult> UpdateAsync(
        Guid id,
        StorageLocationDetails details,
        CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        var location = await dbContext.StorageLocations.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (location is null)
        {
            return OperationError.NotFound<StorageLocation>();
        }

        if (!location.IsFolder && details.IsFolder && await HasBeenUsedAsync(dbContext, id, ct))
        {
            return OperationError.Invalid<StorageLocation>("Использованную складскую позицию нельзя преобразовать в группу.");
        }

        var updateResult = location.UpdateDetails(details);
        if (!updateResult.IsSuccess)
        {
            return updateResult;
        }

        await dbContext.SaveChangesAsync(ct);
        return OperationResult.Success();
    }

    public async Task<OperationResult<IReadOnlyList<StorageLocation>>> GenerateChildrenAsync(
        GenerateStorageLocationsRequest request,
        CancellationToken ct = default)
    {
        var requestValidation = request.Validate();
        if (!requestValidation.IsSuccess)
        {
            return requestValidation.Error!;
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        var contextResult = await ValidateContextAsync(dbContext, request.WarehouseId, request.ZoneId, request.ParentId, ct);
        if (!contextResult.IsSuccess)
        {
            return contextResult.Error!;
        }

        var locationsResult = BuildLocations(request, contextResult.Value);
        if (!locationsResult.IsSuccess)
        {
            return locationsResult.Error!;
        }

        var locations = locationsResult.Value!;
        var codes = locations.Select(location => location.Code!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var hasCodeConflict = codes.Count != locations.Count
            || await dbContext.StorageLocations.AnyAsync(
                location => location.ZoneId == request.ZoneId && codes.Contains(location.Code!),
                ct);

        if (hasCodeConflict)
        {
            return OperationError.Conflict<StorageLocation>("Один или несколько создаваемых кодов уже используются.");
        }

        dbContext.StorageLocations.AddRange(locations);
        await dbContext.SaveChangesAsync(ct);
        return OperationResult<IReadOnlyList<StorageLocation>>.Success(locations);
    }

    public async Task<OperationResult> MarkDeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        var location = await dbContext.StorageLocations.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (location is null)
        {
            return OperationError.NotFound<StorageLocation>();
        }

        if (await dbContext.StorageLocations.AnyAsync(x => x.ParentId == id && !x.DeletionMark, ct))
        {
            return OperationError.Invalid<StorageLocation>("Сначала деактивируйте дочерние позиции.");
        }

        if (!location.IsFolder && await dbContext.InventoryBalances.AnyAsync(x => x.StorageLocationId == id && x.Quantity > 0, ct))
        {
            return OperationError.Invalid<StorageLocation>("Нельзя деактивировать позицию с положительным остатком.");
        }

        location.Deactivate();
        await dbContext.SaveChangesAsync(ct);
        return OperationResult.Success();
    }

    public async Task<OperationResult> UnMarkDeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        var location = await dbContext.StorageLocations.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (location is null)
        {
            return OperationError.NotFound<StorageLocation>();
        }

        if (location.ParentId is Guid parentId
            && !await dbContext.StorageLocations.AnyAsync(x => x.Id == parentId && !x.DeletionMark, ct))
        {
            return OperationError.Invalid<StorageLocation>("Сначала активируйте родительскую позицию.");
        }

        location.Activate();
        await dbContext.SaveChangesAsync(ct);
        return OperationResult.Success();
    }

    public async Task<StorageLocation?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        return await dbContext.StorageLocations.AsNoTracking().Include(x => x.Warehouse).Include(x => x.Zone)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
    }

    public async Task<IReadOnlyList<StorageLocation>> GetTreeAsync(
        Guid zoneId,
        bool includeDeleted,
        CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        var query = dbContext.StorageLocations.AsNoTracking().Include(x => x.Warehouse).Include(x => x.Zone)
            .Where(x => x.ZoneId == zoneId);
        if (!includeDeleted)
        {
            query = query.Where(x => !x.DeletionMark);
        }

        return await query.OrderBy(x => x.Code).ToListAsync(ct);
    }

    public async Task<ListResult<StorageLocation>> ListAsync(
        StorageLocationListQuery listQuery,
        CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        IQueryable<StorageLocation> query = dbContext.StorageLocations.AsNoTracking()
            .Include(x => x.Warehouse).Include(x => x.Zone);
        if (listQuery.ExcludeDeleted)
        {
            query = query.Where(x => !x.DeletionMark);
        }

        if (listQuery.ExcludeFolders)
        {
            query = query.Where(x => !x.IsFolder);
        }

        if (listQuery.WarehouseId is Guid warehouseId)
        {
            query = query.Where(x => x.WarehouseId == warehouseId);
        }

        if (listQuery.ZoneId is Guid zoneId)
        {
            query = query.Where(x => x.ZoneId == zoneId);
        }

        if (listQuery.ZoneType is Domain.Enums.ZoneType zoneType)
        {
            query = query.Where(x => x.Zone!.Type == zoneType);
        }

        if (!string.IsNullOrWhiteSpace(listQuery.SearchString))
        {
            query = query.Where(x => x.Name!.Contains(listQuery.SearchString) || x.Code!.Contains(listQuery.SearchString));
        }

        var totalItems = await query.CountAsync(ct);
        query = listQuery.SortBy switch
        {
            "Name" => listQuery.SortDescending ? query.OrderByDescending(x => x.Name) : query.OrderBy(x => x.Name),
            "Code" => listQuery.SortDescending ? query.OrderByDescending(x => x.Code) : query.OrderBy(x => x.Code),
            _ => query.OrderBy(x => x.Code)
        };
        return new ListResult<StorageLocation>
        {
            Items = await query.Skip(listQuery.Skip).Take(listQuery.Take).ToListAsync(ct),
            TotalItems = totalItems
        };
    }

    private static async Task<OperationResult<StorageLocation?>> ValidateContextAsync(
        ApplicationDbContext dbContext,
        Guid warehouseId,
        Guid zoneId,
        Guid? parentId,
        CancellationToken ct)
    {
        if (!await dbContext.Zones.AnyAsync(x => x.Id == zoneId && x.WarehouseId == warehouseId && !x.DeletionMark, ct))
        {
            return OperationError.Invalid<Zone>("Зона должна быть активна и принадлежать выбранному складу.");
        }

        if (parentId is null)
        {
            return OperationResult<StorageLocation?>.Success(null);
        }

        var parent = await dbContext.StorageLocations.AsNoTracking().FirstOrDefaultAsync(x => x.Id == parentId, ct);
        if (parent is null)
        {
            return OperationError.NotFound<StorageLocation>("Родительская позиция не найдена.");
        }

        if (parent.DeletionMark || parent.WarehouseId != warehouseId || parent.ZoneId != zoneId)
        {
            return OperationError.Invalid<StorageLocation>("Родитель должен быть активен и находиться в той же зоне.");
        }

        return parent;
    }

    private static OperationResult<string> BuildCode(StorageLocation? parent, int number, int segmentWidth)
    {
        return StorageLocation.BuildCode(
            parent?.Code,
            number,
            segmentWidth,
            DefaultConfiguration.Code);
    }

    private static OperationResult<IReadOnlyList<StorageLocation>> BuildLocations(
        GenerateStorageLocationsRequest request,
        StorageLocation? parent)
    {
        var locations = new List<StorageLocation>(request.Count);

        for (var index = 0; index < request.Count; index++)
        {
            var number = request.StartNumber + (index * request.NumberStep);
            var codeResult = StorageLocation.BuildCode(
                parent?.Code,
                number,
                request.SegmentWidth,
                DefaultConfiguration.Code);
            if (!codeResult.IsSuccess)
            {
                return codeResult.Error!;
            }

            var coordinatesResult = BuildCoordinates(request, index);
            if (!coordinatesResult.IsSuccess)
            {
                return coordinatesResult.Error!;
            }

            var detailsResult = StorageLocationDetails.Create(
                $"{request.NamePrefix.Trim()} {number}",
                request.IsFolder,
                request.Dimensions,
                coordinatesResult.Value,
                BuildPickSequence(request, index));
            if (!detailsResult.IsSuccess)
            {
                return detailsResult.Error!;
            }

            var locationResult = StorageLocation.Create(
                Guid.NewGuid(),
                request.WarehouseId,
                request.ZoneId,
                request.ParentId,
                number,
                codeResult.Value!,
                detailsResult.Value!);
            if (!locationResult.IsSuccess)
            {
                return locationResult.Error!;
            }

            locations.Add(locationResult.Value!);
        }

        return OperationResult<IReadOnlyList<StorageLocation>>.Success(locations);
    }

    private static OperationResult<LocationCoordinates> BuildCoordinates(
        GenerateStorageLocationsRequest request,
        int index)
    {
        return LocationCoordinates.Create(
            OffsetCoordinate(
                request.StartCoordinates.X,
                request.CoordinateAxis == CoordinateAxis.X,
                request.CoordinateStep,
                index),
            OffsetCoordinate(
                request.StartCoordinates.Y,
                request.CoordinateAxis == CoordinateAxis.Y,
                request.CoordinateStep,
                index),
            OffsetCoordinate(
                request.StartCoordinates.Z,
                request.CoordinateAxis == CoordinateAxis.Z,
                request.CoordinateStep,
                index));
    }

    private static long? BuildPickSequence(GenerateStorageLocationsRequest request, int index)
    {
        return request.StartPickSequence.HasValue
            ? (long)((decimal)request.StartPickSequence.Value + (index * (decimal)request.PickSequenceStep))
            : null;
    }

    private static double? OffsetCoordinate(double? start, bool selected, double step, int index) =>
        start is null ? null : start + (selected ? step * index : 0);

    private static async Task<bool> HasBeenUsedAsync(ApplicationDbContext dbContext, Guid id, CancellationToken ct) =>
        await dbContext.InventoryBalances.AnyAsync(x => x.StorageLocationId == id, ct)
        || await dbContext.InventoryTurnovers.AnyAsync(x => x.StorageLocationId == id, ct)
        || await dbContext.InventoryMovements.AnyAsync(x => x.SourceStorageLocationId == id || x.DestinationStorageLocationId == id, ct)
        || await dbContext.InventoryCountItems.AnyAsync(x => x.StorageLocationId == id, ct)
        || await dbContext.InventoryTransfers.AnyAsync(x => x.TransitStorageLocationId == id, ct)
        || await dbContext.ReceivingOrders.AnyAsync(x => x.ReceivingLocationId == id, ct)
        || await dbContext.ShippingOrders.AnyAsync(x => x.ShippingLocationId == id, ct);
}

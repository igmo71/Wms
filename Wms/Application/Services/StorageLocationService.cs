using Microsoft.EntityFrameworkCore;
using Wms.Common;
using Wms.Data;
using Wms.Data.Configurations;
using Wms.Domain;

namespace Wms.Application.Services;

public class StorageLocationService(IDbContextFactory<ApplicationDbContext> dbContextFactory)
{
    public async Task<ServiceResult<StorageLocation>> CreateAsync(
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
            return ServiceError.Conflict<StorageLocation>("В выбранной зоне уже есть позиция с таким кодом.");
        }

        StorageLocation location;
        try
        {
            location = StorageLocation.Create(Guid.NewGuid(), request.WarehouseId, request.ZoneId, request.ParentId,
                request.Number, code, request.Name, request.IsFolder, request.Dimensions, request.Coordinates,
                request.PickSequence);
        }
        catch (ArgumentException exception)
        {
            return ServiceError.Invalid<StorageLocation>(exception.Message);
        }

        dbContext.StorageLocations.Add(location);
        await dbContext.SaveChangesAsync(ct);
        return location;
    }

    public async Task<ServiceResult> UpdateAsync(
        Guid id,
        UpdateStorageLocationRequest request,
        CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        var location = await dbContext.StorageLocations.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (location is null)
        {
            return ServiceError.NotFound<StorageLocation>();
        }

        if (!location.IsFolder && request.IsFolder && await HasBeenUsedAsync(dbContext, id, ct))
        {
            return ServiceError.Invalid<StorageLocation>("Использованную складскую позицию нельзя преобразовать в группу.");
        }

        try
        {
            location.UpdateDetails(request.Name, request.IsFolder, request.Dimensions, request.Coordinates,
                request.PickSequence);
        }
        catch (ArgumentException exception)
        {
            return ServiceError.Invalid<StorageLocation>(exception.Message);
        }

        await dbContext.SaveChangesAsync(ct);
        return ServiceResult.Success();
    }

    public async Task<ServiceResult<IReadOnlyList<StorageLocation>>> GenerateChildrenAsync(
        GenerateStorageLocationsRequest request,
        CancellationToken ct = default)
    {
        var requestValidation = ValidateRequest(request);
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

        IReadOnlyList<StorageLocation> locations;
        try
        {
            locations = BuildLocations(request, contextResult.Value);
        }
        catch (ArgumentException exception)
        {
            return ServiceError.Invalid<StorageLocation>(exception.Message);
        }

        var codes = locations.Select(location => location.Code!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var hasCodeConflict = codes.Count != locations.Count
            || await dbContext.StorageLocations.AnyAsync(
                location => location.ZoneId == request.ZoneId && codes.Contains(location.Code!),
                ct);

        if (hasCodeConflict)
        {
            return ServiceError.Conflict<StorageLocation>("Один или несколько создаваемых кодов уже используются.");
        }

        dbContext.StorageLocations.AddRange(locations);
        await dbContext.SaveChangesAsync(ct);
        return ServiceResult<IReadOnlyList<StorageLocation>>.Success(locations);
    }

    public async Task<ServiceResult> MarkDeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        var location = await dbContext.StorageLocations.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (location is null)
        {
            return ServiceError.NotFound<StorageLocation>();
        }

        if (await dbContext.StorageLocations.AnyAsync(x => x.ParentId == id && !x.DeletionMark, ct))
        {
            return ServiceError.Invalid<StorageLocation>("Сначала деактивируйте дочерние позиции.");
        }

        if (!location.IsFolder && await dbContext.InventoryBalances.AnyAsync(x => x.StorageLocationId == id && x.Quantity > 0, ct))
        {
            return ServiceError.Invalid<StorageLocation>("Нельзя деактивировать позицию с положительным остатком.");
        }

        location.Deactivate();
        await dbContext.SaveChangesAsync(ct);
        return ServiceResult.Success();
    }

    public async Task<ServiceResult> UnMarkDeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        var location = await dbContext.StorageLocations.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (location is null)
        {
            return ServiceError.NotFound<StorageLocation>();
        }

        if (location.ParentId is Guid parentId
            && !await dbContext.StorageLocations.AnyAsync(x => x.Id == parentId && !x.DeletionMark, ct))
        {
            return ServiceError.Invalid<StorageLocation>("Сначала активируйте родительскую позицию.");
        }

        location.Activate();
        await dbContext.SaveChangesAsync(ct);
        return ServiceResult.Success();
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

    private static async Task<ServiceResult<StorageLocation?>> ValidateContextAsync(
        ApplicationDbContext dbContext,
        Guid warehouseId,
        Guid zoneId,
        Guid? parentId,
        CancellationToken ct)
    {
        if (!await dbContext.Zones.AnyAsync(x => x.Id == zoneId && x.WarehouseId == warehouseId && !x.DeletionMark, ct))
        {
            return ServiceError.Invalid<Zone>("Зона должна быть активна и принадлежать выбранному складу.");
        }

        if (parentId is null)
        {
            return ServiceResult<StorageLocation?>.Success(null);
        }

        var parent = await dbContext.StorageLocations.AsNoTracking().FirstOrDefaultAsync(x => x.Id == parentId, ct);
        if (parent is null)
        {
            return ServiceError.NotFound<StorageLocation>("Родительская позиция не найдена.");
        }

        if (parent.DeletionMark || parent.WarehouseId != warehouseId || parent.ZoneId != zoneId)
        {
            return ServiceError.Invalid<StorageLocation>("Родитель должен быть активен и находиться в той же зоне.");
        }

        return parent;
    }

    private static ServiceResult<string> BuildCode(StorageLocation? parent, int number, int segmentWidth)
    {
        try
        {
            return StorageLocation.BuildCode(
                parent?.Code,
                number,
                segmentWidth,
                DefaultConfiguration.Code);
        }
        catch (ArgumentException exception)
        {
            return ServiceError.Invalid<StorageLocation>(exception.Message);
        }
    }

    private static ServiceResult ValidateRequest(GenerateStorageLocationsRequest request)
    {
        try
        {
            request.Validate();
            return ServiceResult.Success();
        }
        catch (ArgumentException exception)
        {
            return ServiceError.Invalid<StorageLocation>(exception.Message);
        }
    }

    private static IReadOnlyList<StorageLocation> BuildLocations(
        GenerateStorageLocationsRequest request,
        StorageLocation? parent)
    {
        var locations = new List<StorageLocation>(request.Count);

        for (var index = 0; index < request.Count; index++)
        {
            var number = checked(request.StartNumber + (index * request.NumberStep));
            var code = StorageLocation.BuildCode(
                parent?.Code,
                number,
                request.SegmentWidth,
                DefaultConfiguration.Code);

            locations.Add(StorageLocation.Create(
                Guid.NewGuid(),
                request.WarehouseId,
                request.ZoneId,
                request.ParentId,
                number,
                code,
                $"{request.NamePrefix.Trim()} {number}",
                request.IsFolder,
                CopyDimensions(request.Dimensions),
                BuildCoordinates(request, index),
                BuildPickSequence(request, index)));
        }

        return locations;
    }

    private static LocationCoordinates BuildCoordinates(
        GenerateStorageLocationsRequest request,
        int index)
    {
        return new LocationCoordinates
        {
            X = OffsetCoordinate(
                request.StartCoordinates.X,
                request.CoordinateAxis == CoordinateAxis.X,
                request.CoordinateStep,
                index),
            Y = OffsetCoordinate(
                request.StartCoordinates.Y,
                request.CoordinateAxis == CoordinateAxis.Y,
                request.CoordinateStep,
                index),
            Z = OffsetCoordinate(
                request.StartCoordinates.Z,
                request.CoordinateAxis == CoordinateAxis.Z,
                request.CoordinateStep,
                index)
        };
    }

    private static long? BuildPickSequence(GenerateStorageLocationsRequest request, int index)
    {
        return request.StartPickSequence.HasValue
            ? checked(request.StartPickSequence.Value + (index * request.PickSequenceStep))
            : null;
    }

    private static double? OffsetCoordinate(double? start, bool selected, double step, int index) =>
        start is null ? null : start + (selected ? step * index : 0);

    private static LocationDimensions CopyDimensions(LocationDimensions source) => new()
    {
        Length = source.Length,
        Width = source.Width,
        Height = source.Height,
        Volume = source.Volume,
        VolumeFactor = source.VolumeFactor,
        MaxWeight = source.MaxWeight
    };

    private static async Task<bool> HasBeenUsedAsync(ApplicationDbContext dbContext, Guid id, CancellationToken ct) =>
        await dbContext.InventoryBalances.AnyAsync(x => x.StorageLocationId == id, ct)
        || await dbContext.InventoryTurnovers.AnyAsync(x => x.StorageLocationId == id, ct)
        || await dbContext.InventoryMovements.AnyAsync(x => x.SourceStorageLocationId == id || x.DestinationStorageLocationId == id, ct)
        || await dbContext.InventoryCountItems.AnyAsync(x => x.StorageLocationId == id, ct)
        || await dbContext.InventoryTransfers.AnyAsync(x => x.TransitStorageLocationId == id, ct)
        || await dbContext.ReceivingOrders.AnyAsync(x => x.ReceivingLocationId == id, ct)
        || await dbContext.ShippingOrders.AnyAsync(x => x.ShippingLocationId == id, ct);
}

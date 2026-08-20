using Microsoft.EntityFrameworkCore;
using Wms.Application.StorageLocations;
using Wms.Common;
using Wms.Data;
using Wms.Data.Configurations;
using Wms.Domain;

namespace Wms.Application.StorageLocations;

public class StorageLocationCommandService(IDbContextFactory<ApplicationDbContext> dbContextFactory)
{
    public async Task<OperationResult<StorageLocation>> CreateAsync(
        CreateStorageLocationCommand command,
        CancellationToken ct = default)
    {
        await using ApplicationDbContext dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        OperationResult<StorageLocation?> contextResult = await ValidateContextAsync(dbContext, command.WarehouseId, command.ZoneId, command.ParentId, ct);
        if (!contextResult.IsSuccess)
        {
            return contextResult.Error!;
        }

        OperationResult<string> codeResult = BuildCode(contextResult.Value, command.Number, command.SegmentWidth);
        if (!codeResult.IsSuccess)
        {
            return codeResult.Error!;
        }

        var code = codeResult.Value!;
        if (await dbContext.StorageLocations.AnyAsync(x => x.ZoneId == command.ZoneId && x.Code == code, ct))
        {
            return OperationError.Conflict("В выбранной зоне уже есть позиция с таким кодом.");
        }

        OperationResult<StorageLocation> locationResult = StorageLocation.Create(
            Guid.NewGuid(),
            command.WarehouseId,
            command.ZoneId,
            command.ParentId,
            command.Number,
            code,
            command.Details);

        if (!locationResult.IsSuccess)
        {
            return locationResult.Error!;
        }

        StorageLocation location = locationResult.Value!;
        dbContext.StorageLocations.Add(location);
        await dbContext.SaveChangesAsync(ct);
        return location;
    }

    public async Task<OperationResult> UpdateAsync(
        Guid id,
        StorageLocationDetails details,
        CancellationToken ct = default)
    {
        await using ApplicationDbContext dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        StorageLocation? location = await dbContext.StorageLocations.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (location is null)
        {
            return OperationError.NotFound();
        }

        if (!location.IsFolder && details.IsFolder && await HasBeenUsedAsync(dbContext, id, ct))
        {
            return OperationError.Invalid("Использованную складскую позицию нельзя преобразовать в группу.");
        }

        OperationResult updateResult = location.UpdateDetails(details);
        if (!updateResult.IsSuccess)
        {
            return updateResult;
        }

        await dbContext.SaveChangesAsync(ct);
        return OperationResult.Success();
    }

    public async Task<OperationResult<IReadOnlyList<StorageLocation>>> GenerateChildrenAsync(
        GenerateStorageLocationsCommand command,
        CancellationToken ct = default)
    {
        OperationResult commandValidation = command.Validate();
        if (!commandValidation.IsSuccess)
        {
            return commandValidation.Error!;
        }

        await using ApplicationDbContext dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        OperationResult<StorageLocation?> contextResult = await ValidateContextAsync(dbContext, command.WarehouseId, command.ZoneId, command.ParentId, ct);
        if (!contextResult.IsSuccess)
        {
            return contextResult.Error!;
        }

        OperationResult<IReadOnlyList<StorageLocation>> locationsResult = BuildLocations(command, contextResult.Value);
        if (!locationsResult.IsSuccess)
        {
            return locationsResult.Error!;
        }

        IReadOnlyList<StorageLocation> locations = locationsResult.Value!;
        var codes = locations.Select(location => location.Code!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var hasCodeConflict = codes.Count != locations.Count
            || await dbContext.StorageLocations.AnyAsync(
                location => location.ZoneId == command.ZoneId && codes.Contains(location.Code!),
                ct);

        if (hasCodeConflict)
        {
            return OperationError.Conflict("Один или несколько создаваемых кодов уже используются.");
        }

        dbContext.StorageLocations.AddRange(locations);
        await dbContext.SaveChangesAsync(ct);
        return OperationResult<IReadOnlyList<StorageLocation>>.Success(locations);
    }

    public async Task<OperationResult> MarkDeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using ApplicationDbContext dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        StorageLocation? location = await dbContext.StorageLocations.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (location is null)
        {
            return OperationError.NotFound();
        }

        if (await dbContext.StorageLocations.AnyAsync(x => x.ParentId == id && !x.DeletionMark, ct))
        {
            return OperationError.Invalid("Сначала деактивируйте дочерние позиции.");
        }

        if (!location.IsFolder && await dbContext.InventoryBalances.AnyAsync(x => x.StorageLocationId == id && x.Quantity > 0, ct))
        {
            return OperationError.Invalid("Нельзя деактивировать позицию с положительным остатком.");
        }

        location.Deactivate();
        await dbContext.SaveChangesAsync(ct);
        return OperationResult.Success();
    }

    public async Task<OperationResult> UnMarkDeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using ApplicationDbContext dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        StorageLocation? location = await dbContext.StorageLocations.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (location is null)
        {
            return OperationError.NotFound();
        }

        if (location.ParentId is Guid parentId
            && !await dbContext.StorageLocations.AnyAsync(x => x.Id == parentId && !x.DeletionMark, ct))
        {
            return OperationError.Invalid("Сначала активируйте родительскую позицию.");
        }

        location.Activate();
        await dbContext.SaveChangesAsync(ct);
        return OperationResult.Success();
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
            return OperationError.Invalid("Зона должна быть активна и принадлежать выбранному складу.");
        }

        if (parentId is null)
        {
            return OperationResult<StorageLocation?>.Success(null);
        }

        StorageLocation? parent = await dbContext.StorageLocations.AsNoTracking().FirstOrDefaultAsync(x => x.Id == parentId, ct);
        if (parent is null)
        {
            return OperationError.NotFound("Родительская позиция не найдена.");
        }

        if (parent.DeletionMark || parent.WarehouseId != warehouseId || parent.ZoneId != zoneId)
        {
            return OperationError.Invalid("Родитель должен быть активен и находиться в той же зоне.");
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
        GenerateStorageLocationsCommand command,
        StorageLocation? parent)
    {
        var locations = new List<StorageLocation>(command.Count);

        for (var index = 0; index < command.Count; index++)
        {
            var number = command.StartNumber + (index * command.NumberStep);
            OperationResult<string> codeResult = StorageLocation.BuildCode(
                parent?.Code,
                number,
                command.SegmentWidth,
                DefaultConfiguration.Code);
            if (!codeResult.IsSuccess)
            {
                return codeResult.Error!;
            }

            OperationResult<LocationCoordinates> coordinatesResult = BuildCoordinates(command, index);
            if (!coordinatesResult.IsSuccess)
            {
                return coordinatesResult.Error!;
            }

            OperationResult<StorageLocationDetails> detailsResult = StorageLocationDetails.Create(
                $"{command.NamePrefix.Trim()} {number}",
                command.IsFolder,
                command.Dimensions,
                coordinatesResult.Value,
                BuildPickSequence(command, index));
            if (!detailsResult.IsSuccess)
            {
                return detailsResult.Error!;
            }

            OperationResult<StorageLocation> locationResult = StorageLocation.Create(
                Guid.NewGuid(),
                command.WarehouseId,
                command.ZoneId,
                command.ParentId,
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
        GenerateStorageLocationsCommand command,
        int index)
    {
        return LocationCoordinates.Create(
            OffsetCoordinate(
                command.StartCoordinates.X,
                command.CoordinateAxis == CoordinateAxis.X,
                command.CoordinateStep,
                index),
            OffsetCoordinate(
                command.StartCoordinates.Y,
                command.CoordinateAxis == CoordinateAxis.Y,
                command.CoordinateStep,
                index),
            OffsetCoordinate(
                command.StartCoordinates.Z,
                command.CoordinateAxis == CoordinateAxis.Z,
                command.CoordinateStep,
                index));
    }

    private static long? BuildPickSequence(GenerateStorageLocationsCommand command, int index)
    {
        return command.StartPickSequence.HasValue
            ? (long)((decimal)command.StartPickSequence.Value + (index * (decimal)command.PickSequenceStep))
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

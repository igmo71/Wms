using Microsoft.EntityFrameworkCore;
using Wms.Application.Persistence;
using Wms.Application.StorageLocations;
using Wms.Common;
using Wms.Data;
using Wms.Domain;

namespace Wms.Application.StorageLocations;

public class StorageLocationCommandService(IDbContextFactory<ApplicationDbContext> dbContextFactory)
{
    public async Task<OperationResult<StorageLocation>> CreateAsync(
        CreateStorageLocationCommand command,
        CancellationToken ct = default)
    {
        if (command.Details is null)
            return OperationError.Invalid("Параметры складской позиции обязательны.");

        await using ApplicationDbContext dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        OperationResult<StorageLocation?> contextResult = await ValidateContextAsync(dbContext, command.WarehouseId, command.ZoneId, command.ParentId, command.Details.IsFolder, ct);
        if (!contextResult.IsSuccess)
        {
            return contextResult.Error!;
        }

        OperationResult<string> codeResult = StorageLocationAddress.Normalize(command.Code);
        if (!codeResult.IsSuccess)
        {
            return codeResult.Error!;
        }

        var code = codeResult.Value!;
        if (await dbContext.StorageLocations.AnyAsync(x => x.WarehouseId == command.WarehouseId && x.Code == code, ct))
        {
            return OperationError.Conflict("На этом складе уже есть позиция с таким адресом.");
        }

        OperationResult<StorageLocation> locationResult = StorageLocation.Create(
            Guid.NewGuid(),
            command.WarehouseId,
            command.ZoneId,
            command.ParentId,
            code,
            command.Details);

        if (!locationResult.IsSuccess)
        {
            return locationResult.Error!;
        }

        StorageLocation location = locationResult.Value!;
        dbContext.StorageLocations.Add(location);
        OperationResult saved = await ApplicationPersistence.SaveChangesAsync(dbContext, ct);
        return saved.IsSuccess ? location : saved.Error!;
    }

    public async Task<OperationResult> UpdateAsync(
        Guid id,
        StorageLocationDetails details,
        CancellationToken ct = default)
    {
        await using ApplicationDbContext dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        StorageLocation? location = await dbContext.StorageLocations
            .Include(x => x.ActiveLock)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (location is null)
        {
            return OperationError.NotFound($"Складская позиция '{id}' не найдена.");
        }

        if (!location.IsFolder && details.IsFolder && location.ActiveLock is not null)
        {
            return OperationError.Invalid("Заблокированную складскую позицию нельзя преобразовать в группу.");
        }

        if (!location.IsFolder && details.IsFolder && await HasBeenUsedAsync(dbContext, id, ct))
        {
            return OperationError.Invalid("Использованную складскую позицию нельзя преобразовать в группу.");
        }

        if (location.IsFolder && !details.IsFolder
            && await dbContext.StorageLocations.AnyAsync(x => x.ParentId == id, ct))
        {
            return OperationError.Invalid("Группу с дочерними позициями нельзя преобразовать в место хранения.");
        }

        if (location.AllowsMultipleSkus && !details.AllowsMultipleSkus
            && await dbContext.InventoryBalances
                .Where(x => x.StorageLocationId == id && x.Quantity != 0)
                .Select(x => x.StockKeepingUnitId).Distinct().Take(2).CountAsync(ct) > 1)
        {
            return OperationError.Conflict(
                "Нельзя запретить смешение SKU, пока в ячейке есть ненулевые остатки разных SKU.");
        }

        OperationResult updateResult = location.UpdateDetails(details);
        if (!updateResult.IsSuccess)
        {
            return updateResult;
        }

        return await ApplicationPersistence.SaveChangesAsync(dbContext, ct);
    }

    public async Task<OperationResult> MarkDeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using ApplicationDbContext dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        StorageLocation? location = await dbContext.StorageLocations
            .Include(x => x.ActiveLock)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (location is null)
        {
            return OperationError.NotFound($"Складская позиция '{id}' не найдена.");
        }

        if (await dbContext.StorageLocations.AnyAsync(x => x.ParentId == id && !x.DeletionMark, ct))
        {
            return OperationError.Invalid("Сначала деактивируйте дочерние позиции.");
        }

        if (location.ActiveLock is not null)
        {
            return OperationError.Invalid("Заблокированную складскую позицию нельзя деактивировать.");
        }

        if (!location.IsFolder && await dbContext.InventoryBalances.AnyAsync(x => x.StorageLocationId == id && x.Quantity > 0, ct))
        {
            return OperationError.Invalid("Нельзя деактивировать позицию с положительным остатком.");
        }

        location.Deactivate();
        return await ApplicationPersistence.SaveChangesAsync(dbContext, ct);
    }

    public async Task<OperationResult> UnMarkDeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using ApplicationDbContext dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        StorageLocation? location = await dbContext.StorageLocations
            .Include(x => x.Warehouse)
            .Include(x => x.Zone)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (location is null)
        {
            return OperationError.NotFound($"Складская позиция '{id}' не найдена.");
        }

        if (location.Warehouse is null
            || location.Warehouse.DeletionMark
            || (!location.IsFolder && (location.Zone is null || location.Zone.DeletionMark))
            || (location.Zone is not null && location.Zone.DeletionMark))
        {
            return OperationError.Invalid(
                "Складскую позицию можно активировать только в активном складе и, если это ячейка, в активной зоне.");
        }

        if (location.ParentId is Guid parentId
            && !await dbContext.StorageLocations.AnyAsync(x => x.Id == parentId && !x.DeletionMark, ct))
        {
            return OperationError.Invalid("Сначала активируйте родительскую позицию.");
        }

        location.Activate();
        return await ApplicationPersistence.SaveChangesAsync(dbContext, ct);
    }

    private static async Task<OperationResult<StorageLocation?>> ValidateContextAsync(
        ApplicationDbContext dbContext,
        Guid warehouseId,
        Guid? zoneId,
        Guid? parentId,
        bool isFolder,
        CancellationToken ct)
    {
        if (!await dbContext.Warehouses.AnyAsync(x => x.Id == warehouseId && !x.DeletionMark, ct))
            return OperationError.Invalid("Выберите активный склад.");

        if (!isFolder && zoneId is null)
            return OperationError.Invalid("Для места хранения выберите функциональную зону.");

        if (zoneId is Guid selectedZoneId && !await dbContext.Zones.AnyAsync(
            x => x.Id == selectedZoneId && x.WarehouseId == warehouseId && !x.DeletionMark,
            ct))
        {
            return OperationError.Invalid(
                "Зона должна быть активна и принадлежать выбранному складу.");
        }

        if (parentId is null)
        {
            return OperationResult<StorageLocation?>.Success(null);
        }

        StorageLocation? parent = await dbContext.StorageLocations.AsNoTracking().FirstOrDefaultAsync(x => x.Id == parentId, ct);
        if (parent is null)
        {
            return OperationError.NotFound($"Родительская складская позиция '{parentId}' не найдена.");
        }

        if (parent.DeletionMark || parent.WarehouseId != warehouseId || !parent.IsFolder)
        {
            return OperationError.Invalid("Родитель должен быть активной группой того же склада.");
        }

        return parent;
    }

    private static async Task<bool> HasBeenUsedAsync(ApplicationDbContext dbContext, Guid id, CancellationToken ct) =>
        await dbContext.InventoryBalances.AnyAsync(x => x.StorageLocationId == id, ct)
        || await dbContext.InventoryTurnovers.AnyAsync(x => x.StorageLocationId == id, ct)
        || await dbContext.InventoryMovements.AnyAsync(x => x.SourceStorageLocationId == id || x.DestinationStorageLocationId == id, ct)
        || await dbContext.InventoryCounts.AnyAsync(x => x.StorageLocationId == id, ct)
        || await dbContext.InventoryTransfers.AnyAsync(x => x.TransitStorageLocationId == id, ct)
        || await dbContext.ReceivingOrders.AnyAsync(x => x.ReceivingLocationId == id, ct)
        || await dbContext.ShippingOrders.AnyAsync(x => x.ShippingLocationId == id, ct);
}

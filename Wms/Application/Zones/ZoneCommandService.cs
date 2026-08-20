using Microsoft.EntityFrameworkCore;
using Wms.Application.Zones;
using Wms.Common;
using Wms.Data;
using Wms.Domain;

namespace Wms.Application.Zones;

public class ZoneCommandService(IDbContextFactory<ApplicationDbContext> dbContextFactory)
{
    public async Task<OperationResult<Zone>> SaveAsync(
        SaveZoneCommand command,
        CancellationToken ct = default)
    {
        await using ApplicationDbContext dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        Zone? zone = await FindForUpdateAsync(dbContext, command.Id, ct);

        if (command.Id.HasValue && zone is null)
        {
            return OperationError.NotFound<Zone>();
        }

        Guid? originalWarehouseId = zone?.WarehouseId;

        OperationResult<Zone> domainResult = ApplyCommand(zone, command);
        if (!domainResult.IsSuccess)
        {
            return domainResult.Error!;
        }

        zone = domainResult.Value!;
        if (!command.Id.HasValue)
        {
            dbContext.Zones.Add(zone);
        }

        OperationResult stateValidation = await ValidateStateAsync(
            dbContext,
            zone,
            originalWarehouseId,
            ct);

        if (!stateValidation.IsSuccess)
        {
            return stateValidation.Error!;
        }

        await dbContext.SaveChangesAsync(ct);
        return zone;
    }

    public async Task<int> MarkDeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using ApplicationDbContext dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        Zone? zone = await dbContext.Zones.FirstOrDefaultAsync(x => x.Id == id, ct);

        if (zone is null)
        {
            return 0;
        }

        zone.Deactivate();
        return await dbContext.SaveChangesAsync(ct);
    }

    public async Task<int> UnMarkDeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using ApplicationDbContext dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        Zone? zone = await dbContext.Zones.FirstOrDefaultAsync(x => x.Id == id, ct);

        if (zone is null)
        {
            return 0;
        }

        zone.Activate();
        return await dbContext.SaveChangesAsync(ct);
    }

    private static Task<Zone?> FindForUpdateAsync(
        ApplicationDbContext dbContext,
        Guid? id,
        CancellationToken ct)
    {
        return id.HasValue
            ? dbContext.Zones.FirstOrDefaultAsync(x => x.Id == id.Value, ct)
            : Task.FromResult<Zone?>(null);
    }

    private static OperationResult<Zone> ApplyCommand(Zone? zone, SaveZoneCommand command)
    {
        if (zone is null)
        {
            return Zone.Create(
                Guid.NewGuid(),
                command.WarehouseId,
                command.Code,
                command.Name,
                command.Type);
        }

        OperationResult warehouseResult = zone.MoveToWarehouse(command.WarehouseId);
        if (!warehouseResult.IsSuccess)
        {
            return warehouseResult.Error!;
        }

        OperationResult detailsResult = zone.UpdateDetails(command.Code, command.Name, command.Type);
        if (!detailsResult.IsSuccess)
        {
            return detailsResult.Error!;
        }

        return zone;
    }

    private static async Task<OperationResult> ValidateStateAsync(
        ApplicationDbContext dbContext,
        Zone zone,
        Guid? originalWarehouseId,
        CancellationToken ct)
    {
        var codeIsUsed = await dbContext.Zones.AnyAsync(
            x => x.WarehouseId == zone.WarehouseId
                && x.Code == zone.Code
                && x.Id != zone.Id,
            ct);

        if (codeIsUsed)
        {
            return OperationError.Conflict<Zone>("В выбранном складе уже есть зона с таким кодом.");
        }

        var changesWarehouse = originalWarehouseId.HasValue
            && originalWarehouseId.Value != zone.WarehouseId;

        if (changesWarehouse
            && await dbContext.StorageLocations.AnyAsync(x => x.ZoneId == zone.Id, ct))
        {
            return OperationError.Invalid<Zone>(
                "Зону со складскими позициями нельзя перенести в другой склад.");
        }

        return OperationResult.Success();
    }

}

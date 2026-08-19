using Microsoft.EntityFrameworkCore;
using Wms.Application.Zones;
using Wms.Common;
using Wms.Data;
using Wms.Domain;

namespace Wms.Application.Services;

public class ZoneService(IDbContextFactory<ApplicationDbContext> dbContextFactory)
{
    public async Task<ServiceResult<Zone>> SaveAsync(
        SaveZoneRequest request,
        CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        var zone = await FindForUpdateAsync(dbContext, request.Id, ct);

        if (request.Id.HasValue && zone is null)
        {
            return ServiceError.NotFound<Zone>();
        }

        var originalWarehouseId = zone?.WarehouseId;

        var domainResult = DomainOperation.Execute(() => ApplyRequest(zone, request));
        if (!domainResult.IsSuccess)
        {
            return domainResult.Error!;
        }

        zone = domainResult.Value!;
        if (!request.Id.HasValue)
        {
            dbContext.Zones.Add(zone);
        }

        var stateValidation = await ValidateStateAsync(
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
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        var zone = await dbContext.Zones.FirstOrDefaultAsync(x => x.Id == id, ct);

        if (zone is null)
        {
            return 0;
        }

        zone.Deactivate();
        return await dbContext.SaveChangesAsync(ct);
    }

    public async Task<int> UnMarkDeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        var zone = await dbContext.Zones.FirstOrDefaultAsync(x => x.Id == id, ct);

        if (zone is null)
        {
            return 0;
        }

        zone.Activate();
        return await dbContext.SaveChangesAsync(ct);
    }

    public async Task<Zone?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        return await dbContext.Zones
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, ct);
    }

    public async Task<ListResult<Zone>> ListAsync(
        ZoneListQuery listQuery,
        CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        IQueryable<Zone> query = dbContext.Zones
            .AsNoTracking()
            .Include(x => x.Warehouse);

        if (listQuery.ExcludeDeleted)
        {
            query = query.Where(x => !x.DeletionMark);
        }

        query = ApplySearch(query, listQuery);
        var totalItems = await query.CountAsync(ct);
        query = ApplySorting(query, listQuery.SortBy, listQuery.SortDescending);

        var items = await query
            .Skip(listQuery.Skip)
            .Take(listQuery.Take)
            .ToListAsync(ct);

        return new ListResult<Zone>
        {
            Items = items,
            TotalItems = totalItems
        };
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

    private static Zone ApplyRequest(Zone? zone, SaveZoneRequest request)
    {
        if (zone is null)
        {
            return Zone.Create(
                Guid.NewGuid(),
                request.WarehouseId,
                request.Code,
                request.Name,
                request.Type);
        }

        zone.MoveToWarehouse(request.WarehouseId);
        zone.UpdateDetails(request.Code, request.Name, request.Type);
        return zone;
    }

    private static async Task<ServiceResult> ValidateStateAsync(
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
            return ServiceError.Conflict<Zone>("В выбранном складе уже есть зона с таким кодом.");
        }

        var changesWarehouse = originalWarehouseId.HasValue
            && originalWarehouseId.Value != zone.WarehouseId;

        if (changesWarehouse
            && await dbContext.StorageLocations.AnyAsync(x => x.ZoneId == zone.Id, ct))
        {
            return ServiceError.Invalid<Zone>(
                "Зону со складскими позициями нельзя перенести в другой склад.");
        }

        return ServiceResult.Success();
    }

    private static IQueryable<Zone> ApplySearch(
        IQueryable<Zone> query,
        ZoneListQuery listQuery)
    {
        if (!string.IsNullOrWhiteSpace(listQuery.SearchString))
        {
            query = query.Where(x => x.Name!.Contains(listQuery.SearchString)
                || x.Code!.Contains(listQuery.SearchString));
        }

        if (listQuery.WarehouseId is Guid warehouseId)
        {
            query = query.Where(x => x.WarehouseId == warehouseId);
        }

        if (listQuery.Type is Domain.Enums.ZoneType type)
        {
            query = query.Where(x => x.Type == type);
        }

        return query;
    }

    private static IQueryable<Zone> ApplySorting(
        IQueryable<Zone> query,
        string? sortBy,
        bool sortDescending)
    {
        return sortBy switch
        {
            "Name" => sortDescending
                ? query.OrderByDescending(x => x.Name)
                : query.OrderBy(x => x.Name),
            "Type" => sortDescending
                ? query.OrderByDescending(x => x.Type)
                : query.OrderBy(x => x.Type),
            _ => query.OrderByDescending(x => x.Name)
        };
    }
}

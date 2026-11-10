using Microsoft.EntityFrameworkCore;
using Wms.Application.Picking;
using Wms.Domain.Enums;

namespace Wms.Data;

public sealed class PickingAssemblyQueryStore(
    IDbContextFactory<ApplicationDbContext> contextFactory) : IPickingAssemblyQueryStore
{
    public async Task<IReadOnlyList<PickingAssemblyListItem>> ListForOperatorAsync(
        Guid warehouseId, string userId, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var assemblies = await db.PickingAssemblies.AsNoTracking()
            .Where(x => x.WarehouseId == warehouseId
                && x.Status != PickingAssemblyStatus.Cancelled
                && (x.CreatedBy == userId || x.ResponsibleUserId == userId))
            .Include(x => x.SourceLinks).ThenInclude(x => x.ShippingOrder)
            .OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id)
            .AsSplitQuery().ToArrayAsync(ct);
        if (assemblies.Length == 0)
            return [];
        var ids = assemblies.Select(x => x.ResponsibleUserId).Distinct().ToArray();
        var users = await db.Users.AsNoTracking().Where(x => ids.Contains(x.Id))
            .Select(x => new { x.Id, x.DisplayName, x.UserName }).ToArrayAsync(ct);
        var names = users.ToDictionary(x => x.Id,
            x => DisplayName(x.DisplayName, x.UserName, x.Id));

        return assemblies.Select(x => new PickingAssemblyListItem(
            x.Id, x.Number, x.WarehouseId, x.Status,
            x.OperationalRevision, x.CreatedBy, x.ResponsibleUserId,
            names.GetValueOrDefault(x.ResponsibleUserId, x.ResponsibleUserId),
            x.CreatedAtUtc, x.StartedAtUtc,
            x.ActiveSources.Select(link => new PickingAssemblyListSource(
                link.ShippingOrderId, link.ShippingOrder?.Number,
                link.StartConfirmedAtUtc is not null, link.LastStartError,
                link.ShippingOrder?.LastSourceCheckError)).ToArray()))
            .ToArray();
    }

    public async Task<PickingAssemblyView?> GetAsync(Guid assemblyId, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var assembly = await db.PickingAssemblies.AsNoTracking()
            .Include(x => x.SourceLinks).ThenInclude(x => x.ShippingOrder)
            .Include(x => x.PlanLines).ThenInclude(x => x.StockKeepingUnit)
            .AsSplitQuery().SingleOrDefaultAsync(x => x.Id == assemblyId, ct);
        if (assembly is null)
            return null;
        var owner = await db.Users.AsNoTracking()
            .Where(x => x.Id == assembly.ResponsibleUserId)
            .Select(x => new { x.DisplayName, x.UserName })
            .SingleOrDefaultAsync(ct);
        var ownerName = DisplayName(owner?.DisplayName, owner?.UserName,
            assembly.ResponsibleUserId);

        return new PickingAssemblyView(
            assembly.Id, assembly.Number, assembly.WarehouseId, assembly.Status,
            assembly.OperationalRevision, assembly.CreatedBy,
            assembly.ResponsibleUserId, ownerName, assembly.CreatedAtUtc,
            assembly.FirstStartAttemptAtUtc, assembly.StartedAtUtc, assembly.ClosedAtUtc,
            assembly.ActiveSources.Select(link =>
            {
                var source = link.ShippingOrder;
                return new PickingAssemblySourceView(
                    link.ShippingOrderId, source?.Number,
                    source?.Status ?? ShippingOrderStatus.Unknown,
                    source?.Posted ?? false, source?.DeletionMark ?? false,
                    source?.LastSourceCheckAtUtc, source?.LastSourceCheckError,
                    link.StartConfirmedAtUtc, link.LastStartAttemptAtUtc,
                    link.LastStartError, link.PlanSourceSnapshotJson,
                    source?.CurrentSourceJson);
            }).ToArray(),
            assembly.PlanLines.Select(line => new PickingAssemblyPlanLineView(
                line.ShippingOrderId, line.StockKeepingUnitId,
                line.StockKeepingUnit?.Code, line.StockKeepingUnit?.Name,
                line.Quantity)).ToArray());
    }

    private static string DisplayName(string? displayName, string? userName, string id) =>
        !string.IsNullOrWhiteSpace(displayName) ? displayName
        : !string.IsNullOrWhiteSpace(userName) ? userName : id;
}

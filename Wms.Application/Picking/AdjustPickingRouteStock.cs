using Wms.Application.CommandExecution;
using Wms.Application.Inventory;
using Wms.Common;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Application.Picking;

public sealed class AdjustPickingRouteStock(IPickingAssemblyStore assemblies,
    IStockAvailabilityStore stock, BuildPickingRoute routeBuilder)
{
    public OperationResult<decimal> Consume(ICommandExecutionContext session,
        PickingStockChangeState state, PickingSourceView source, decimal quantity)
    {
        var own = state.Reservations.Where(x => x.PickingAssemblyId == source.PickingAssemblyId).ToArray();
        var current = own.SingleOrDefault(x => IsSourceGroup(x, source));
        var used = Math.Min(quantity, current?.Quantity ?? 0);
        if (current is not null && used > 0)
        {
            var released = Release(session, state.Locations.Single(x => x.Location.Id == current.StorageLocationId),
                current, used);
            if (!released.IsSuccess) return released.Error!;
        }
        return ReleaseAlternatives(session, state.Locations, own.Where(x => x != current), quantity - used);
    }

    public OperationResult<IReadOnlyDictionary<Guid, decimal>> ReleaseShortGroup(ICommandExecutionContext session,
        PickingStockChangeState state, PickingSourceView source, decimal found)
    {
        var disputed = state.Reservations.Where(x => IsSourceGroup(x, source)).ToArray();
        var location = state.Locations.Single(x => x.Location.Id == source.StorageLocationId);
        var uncovered = disputed.ToDictionary(x => x.PickingAssemblyId, x => x.Quantity);
        foreach (var reservation in disputed)
        {
            var released = Release(session, location, reservation, reservation.Quantity);
            if (!released.IsSuccess) return released.Error!;
        }

        var lost = uncovered.GetValueOrDefault(source.PickingAssemblyId);
        var alternatives = state.Reservations.Where(x => x.PickingAssemblyId == source.PickingAssemblyId
            && !IsSourceGroup(x, source));
        var excess = ReleaseAlternatives(session, state.Locations, alternatives, Math.Max(0, found - lost));
        if (!excess.IsSuccess) return excess.Error!;
        uncovered[source.PickingAssemblyId] = Math.Max(0, lost - found) + excess.Value;
        return OperationResult<IReadOnlyDictionary<Guid, decimal>>.Success(uncovered);
    }

    public async Task<OperationResult> ReplaceAsync(ICommandExecutionContext session,
        PickingWorkState work, IReadOnlyList<PickingRouteStockLocation> locations,
        Guid skuId, decimal limit, DateOnly today, CancellationToken ct)
    {
        if (limit <= 0)
            return OperationResult.Success();
        var assembly = work.Assembly!;
        var availability = await stock.ReadSkuAsync(session, assembly.WarehouseId, skuId, ct);
        var needed = Math.Min(limit, Uncovered(work, skuId, availability));
        if (needed <= 0)
            return OperationResult.Success();

        var byId = locations.ToDictionary(x => x.Location.Id);
        var route = routeBuilder.Execute(new PickingRouteRequest(assembly.Id, assembly.WarehouseId, today,
            new Dictionary<Guid, decimal> { [skuId] = needed }, availability, locations.Select(x => x.Route).ToArray()));
        if (!route.IsSuccess)
            return route.Error!;
        foreach (var step in route.Value!.Steps)
        {
            var group = new StockGroupKey(assembly.WarehouseId, step.StorageLocationId, skuId, step.ExpirationDate);
            var added = await AddToGroupAsync(session, assembly.Id, group,
                byId[step.StorageLocationId].Location, step.Quantity, ct);
            if (!added.IsSuccess) return added;
        }
        return OperationResult.Success();
    }

    public async Task<OperationResult<decimal>> AllocateReplenishmentAsync(
        ICommandExecutionContext session, Guid assemblyId, StorageLocation destination,
        StockGroupKey group, decimal transferred, IReadOnlyList<StockAvailability> beforeTransfer, CancellationToken ct)
    {
        var owners = await assemblies.LoadRouteOwnersAsync(session, [assemblyId], group.StockKeepingUnitId, ct);
        var work = owners.SingleOrDefault();
        if (work?.Assembly is not { } assembly)
            return 0m;

        var before = beforeTransfer.SingleOrDefault(x => x.Group == group);
        var freeAfterPosting = (before?.AccountingQuantity ?? 0) + transferred
            - (before?.ReservedQuantity ?? 0) - (before?.BlockedQuantity ?? 0);
        var quantity = Math.Min(transferred, Math.Min(freeAfterPosting,
            Uncovered(work, group.StockKeepingUnitId, beforeTransfer)));
        if (quantity <= 0)
            return 0m;

        var changed = assembly.RecordRouteAdjustment();
        if (!changed.IsSuccess) return changed.Error!;
        var added = await AddToGroupAsync(session, assembly.Id, group, destination, quantity, ct);
        return added.IsSuccess ? quantity : added.Error!;
    }

    public async Task<OperationResult> ApplyPlanChangeAsync(ICommandExecutionContext session,
        PickingWorkState work, IReadOnlyList<PickingRouteStockLocation> locations,
        Guid skuId, decimal planChange, DateOnly today, CancellationToken ct)
    {
        if (planChange > 0)
            return await ReplaceAsync(session, work, locations, skuId, planChange, today, ct);
        var reservations = await assemblies.LoadOwnRouteReservationsAsync(session, work.Assembly!.Id, skuId, ct);
        var excess = reservations.Sum(x => x.Quantity) - Math.Max(0, Remaining(work, skuId));
        if (excess <= 0)
            return OperationResult.Success();

        var locationIds = locations.Select(x => x.Location.Id).ToHashSet();
        if (reservations.Any(x => !locationIds.Contains(x.StorageLocationId)))
            return OperationError.Conflict("Состав закреплений изменился. Повторите изменение плана.");
        var released = ReleaseAlternatives(session, locations, reservations, excess, releaseHeldPallets: true);
        if (!released.IsSuccess) return released.Error!;
        return await ReplaceAsync(session, work, locations, skuId, released.Value, today, ct);
    }

    public async Task<OperationResult> AllocateReturnedAsync(ICommandExecutionContext session,
        PickingStockChangeState state, PickingEntry entry, decimal returned, DateOnly today, CancellationToken ct)
    {
        var work = state.Work;
        var assembly = work.Assembly!;
        var location = work.Location!;
        if (location.ActiveLock is { OwnerType: StorageLocationLockOwnerType.PickingAssembly } hold
            && hold.OwnerId == assembly.Id && (entry.ExpirationDate is null || entry.ExpirationDate >= today))
        {
            var availability = await stock.ReadSkuAsync(session, assembly.WarehouseId, entry.StockKeepingUnitId, ct);
            var group = new StockGroupKey(assembly.WarehouseId, location.Id, entry.StockKeepingUnitId, entry.ExpirationDate);
            var free = availability.SingleOrDefault(x => x.Group == group)?.AvailableForNewPick(assembly.Id) ?? 0;
            var quantity = Math.Min(returned, Math.Min(free, Uncovered(work, entry.StockKeepingUnitId, availability)));
            if (quantity > 0 && quantity == free)
                return await AddToGroupAsync(session, assembly.Id, group, location, quantity, ct);
        }

        return await ReplaceAsync(session, work, state.Locations, entry.StockKeepingUnitId, returned, today, ct);
    }

    private async Task<OperationResult> AddToGroupAsync(ICommandExecutionContext session, Guid assemblyId,
        StockGroupKey group, StorageLocation location, decimal quantity, CancellationToken ct)
    {
        var current = await assemblies.FindRouteReservationAsync(session, assemblyId, group, ct);
        if (current is null)
        {
            var created = PickingRouteReservation.Create(assemblyId, group.WarehouseId,
                group.StorageLocationId, group.StockKeepingUnitId, group.ExpirationDate, quantity);
            if (!created.IsSuccess) return created.Error!;
            session.Stage(created.Value!);
        }
        else
        {
            var changed = current.ChangeQuantity(current.Quantity + quantity);
            if (!changed.IsSuccess) return changed;
        }
        location.AdvanceOperationalRevision();
        return OperationResult.Success();
    }

    private static decimal Uncovered(PickingWorkState work, Guid skuId, IReadOnlyList<StockAvailability> availability)
        => Remaining(work, skuId) - availability.Sum(x => x.RouteReservedFor(work.Assembly!.Id));

    private static decimal Remaining(PickingWorkState work, Guid skuId) =>
        work.Assembly!.PlanLines.Where(x => x.StockKeepingUnitId == skuId).Sum(x => x.Quantity)
        - work.Entries.Where(x => x.StockKeepingUnitId == skuId).Sum(x => x.Quantity);

    private OperationResult<decimal> ReleaseAlternatives(ICommandExecutionContext session,
        IReadOnlyList<PickingRouteStockLocation> locations,
        IEnumerable<PickingRouteReservation> reservations, decimal quantity, bool releaseHeldPallets = false)
    {
        if (quantity <= 0)
            return 0m;
        var byId = locations.ToDictionary(x => x.Location.Id);
        var candidates = reservations.Where(x => releaseHeldPallets
                || byId[x.StorageLocationId].Location.ActiveLock is null)
            .OrderBy(x => byId[x.StorageLocationId].Route.ZoneType == ZoneType.Reserve)
            .ThenByDescending(x => x.ExpirationDate is null).ThenByDescending(x => x.ExpirationDate)
            .ThenBy(x => x.StorageLocationId).ToArray();
        foreach (var row in candidates.Where(x => byId[x.StorageLocationId].Route.ZoneType != ZoneType.Reserve))
        {
            var part = Math.Min(quantity, row.Quantity);
            var released = Release(session, byId[row.StorageLocationId], row, part);
            if (!released.IsSuccess) return released.Error!;
            quantity -= part;
            if (quantity == 0) return 0m;
        }
        foreach (var pallet in candidates.Where(x => byId[x.StorageLocationId].Route.ZoneType == ZoneType.Reserve)
            .GroupBy(x => x.StorageLocationId))
        {
            // A Reserve route is for a whole pallet. Any remaining need is allocated again.
            foreach (var row in pallet)
            {
                var released = Release(session, byId[pallet.Key], row, row.Quantity);
                if (!released.IsSuccess) return released.Error!;
                quantity -= row.Quantity;
            }
            if (quantity <= 0) return -quantity;
        }
        return 0m;
    }

    private OperationResult Release(ICommandExecutionContext session, PickingRouteStockLocation location,
        PickingRouteReservation reservation, decimal quantity)
    {
        if (quantity == reservation.Quantity)
            assemblies.RemoveRouteReservation(session, reservation);
        else
        {
            var changed = reservation.ChangeQuantity(reservation.Quantity - quantity);
            if (!changed.IsSuccess) return changed;
        }
        location.Location.AdvanceOperationalRevision();
        return OperationResult.Success();
    }

    private static bool IsSourceGroup(PickingRouteReservation row, PickingSourceView source) =>
        row.StorageLocationId == source.StorageLocationId && row.StockKeepingUnitId == source.StockKeepingUnitId
        && row.ExpirationDate == source.ExpirationDate;
}

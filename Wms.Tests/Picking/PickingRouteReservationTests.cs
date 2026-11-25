using Microsoft.EntityFrameworkCore;
using Wms.Application.CommandExecution;
using Wms.Application.Picking;
using Wms.Common;
using Wms.Data;
using Wms.Domain;
using Wms.Domain.Enums;
using Wms.Tests.Infrastructure;
using static Wms.Tests.Infrastructure.TestSupport;
using static Wms.Tests.Replenishment.ReplenishmentTestScenario;

namespace Wms.Tests.Picking;

[Collection(SqlDatabaseCollection.Name)]
public sealed class PickingRouteReservationTests(SqlDatabase database)
{
    [Fact]
    public async Task Picking_from_an_alternative_releases_only_the_replaced_quantity_once()
    {
        var warehouse = new Warehouse { Id = Guid.NewGuid(), Name = "Alternative pick warehouse" };
        var sku = new StockKeepingUnit { Id = Guid.NewGuid(), Name = "Picking SKU" };
        var zone = Zone(warehouse.Id, "PK", ZoneType.Storage);
        var original = Location(warehouse.Id, zone, 1);
        var alternative = Location(warehouse.Id, zone, 2);
        var (assembly, source) = ActiveAssembly(warehouse.Id, sku.Id, 6);
        var (other, otherSource) = ActiveAssembly(warehouse.Id, sku.Id, 2);
        await using (var seed = database.CreateDbContext())
        {
            seed.AddRange(warehouse, sku, zone, original, alternative, assembly, source, other, otherSource,
                Balance(warehouse.Id, original.Id, sku.Id, 6),
                Balance(warehouse.Id, alternative.Id, sku.Id, 4),
                Value(PickingRouteReservation.Create(assembly.Id, warehouse.Id, original.Id, sku.Id, null, 6)),
                Value(PickingRouteReservation.Create(other.Id, warehouse.Id, alternative.Id, sku.Id, null, 2)));
            await seed.SaveChangesAsync();
        }

        var (scan, pick, _) = Commands();
        var scanned = Value(await scan.ExecuteAsync(assembly.Id, sku.Id, alternative.Code, Worker));
        Assert.Equal(2m, scanned.SuggestedQuantity);
        Assert.Equal(4m, scanned.ExpectedPhysicalQuantity);
        var request = new ConfirmPickRequest(Guid.NewGuid(), assembly.Id, assembly.OperationalRevision,
            alternative.Code, sku.Id, null, 2, Worker);
        Error(await pick.ExecuteAsync(request with { RequestId = Guid.NewGuid(), Quantity = 3 }),
            OperationErrorType.Conflict);
        await using (var rejected = database.CreateDbContext())
            Assert.Equal(6m, (await rejected.PickingRouteReservations
                .SingleAsync(x => x.PickingAssemblyId == assembly.Id)).Quantity);

        var entryId = Value(await pick.ExecuteAsync(request));
        Assert.Equal(entryId, Value(await pick.ExecuteAsync(request)));
        await using var verify = database.CreateDbContext();
        var own = await verify.PickingRouteReservations.SingleAsync(x => x.PickingAssemblyId == assembly.Id);
        Assert.Equal(original.Id, own.StorageLocationId);
        Assert.Equal(4m, own.Quantity);
        Assert.Equal(2m, (await verify.PickingRouteReservations.SingleAsync(x => x.PickingAssemblyId == other.Id)).Quantity);
        var fact = await verify.PickingReservations.SingleAsync(x => x.PickingAssemblyId == assembly.Id);
        Assert.Equal(entryId, fact.PickingEntryId);
        Assert.Equal(alternative.Id, fact.StorageLocationId);
        Assert.Equal(2m, fact.Quantity);
        Assert.Empty(await verify.InventoryMovements.Where(x => x.RecorderId == assembly.Id).ToArrayAsync());

        var revision = await verify.PickingAssemblies.Where(x => x.Id == assembly.Id)
            .Select(x => x.OperationalRevision).SingleAsync();
        var cancel = new CancelPickingAssembly(new CommandExecutor(new CommandExecutionStore(database)),
            new PickingAssemblyStore(database), Permission(), TimeProvider.System);
        Error(await cancel.ExecuteAsync(new(Guid.NewGuid(), assembly.Id, revision, Manager)),
            OperationErrorType.Conflict);
        Assert.Equal(4m, await verify.PickingRouteReservations.Where(x => x.PickingAssemblyId == assembly.Id)
            .Select(x => x.Quantity).SingleAsync());
        var remaining = Value(await scan.ExecuteAsync(assembly.Id, sku.Id, original.Code, Worker));
        Assert.Equal(4m, remaining.SuggestedQuantity);
        var finalEntry = Value(await pick.ExecuteAsync(new ConfirmPickRequest(
            Guid.NewGuid(), assembly.Id, revision, original.Code, sku.Id, null, 4, Worker)));
        Assert.False(await verify.PickingRouteReservations.AnyAsync(x => x.PickingAssemblyId == assembly.Id));
        Assert.Equal(4m, (await verify.PickingReservations.SingleAsync(x => x.PickingEntryId == finalEntry)).Quantity);
    }

    [Fact]
    public async Task Plan_change_releases_excess_and_allocates_new_demand()
    {
        var warehouse = new Warehouse { Id = Guid.NewGuid(), Name = "Picking plan warehouse" };
        var sku = new StockKeepingUnit { Id = Guid.NewGuid(), Name = "Planned SKU" };
        var addedSku = new StockKeepingUnit { Id = Guid.NewGuid(), Name = "Added SKU" };
        var zone = Zone(warehouse.Id, "PK", ZoneType.Storage);
        var location = Location(warehouse.Id, zone, 1);
        var addedLocation = Location(warehouse.Id, zone, 2);
        var (assembly, source) = ActiveAssembly(warehouse.Id, sku.Id, 8);
        const string reviewed = "{}";
        Success(source.RecordSourceCheck(reviewed, null, DateTimeOffset.UtcNow));
        await using (var seed = database.CreateDbContext())
        {
            seed.AddRange(warehouse, sku, addedSku, zone, location, addedLocation, assembly, source,
                Balance(warehouse.Id, location.Id, sku.Id, 10),
                Balance(warehouse.Id, addedLocation.Id, addedSku.Id, 2),
                Value(PickingRouteReservation.Create(assembly.Id, warehouse.Id, location.Id, sku.Id, null, 8)));
            await seed.SaveChangesAsync();
        }

        var assemblies = new PickingAssemblyStore(database);
        var stock = new StockAvailabilityStore(database);
        var routeStock = new AdjustPickingRouteStock(assemblies, stock, new BuildPickingRoute());
        var change = new ChangePickingPlan(new CommandExecutor(new CommandExecutionStore(database)),
            assemblies, new ReplenishmentStore(database), Permission(), routeStock, TimeProvider.System);
        var request = new ChangePickingPlanRequest(Guid.NewGuid(), assembly.Id, source.Id, assembly.OperationalRevision,
            new Dictionary<Guid, decimal> { [sku.Id] = 5, [addedSku.Id] = 2 }, reviewed, "Изменение заказа", Manager);
        Value(await change.ExecuteAsync(request));
        var reduced = Assert.Single(await stock.ReadSkuAsync(warehouse.Id, sku.Id, default));
        Assert.Equal(5m, reduced.AvailableForRoute(assembly.Id));
        Assert.Equal(5m, reduced.AvailableQuantity);
        var added = Assert.Single(await stock.ReadSkuAsync(warehouse.Id, addedSku.Id, default));
        Assert.Equal(2m, added.AvailableForRoute(assembly.Id));

        var current = (await assemblies.ReadAssemblyAsync(assembly.Id, default))!;
        Value(await change.ExecuteAsync(request with
        {
            RequestId = Guid.NewGuid(),
            ExpectedRevision = current.OperationalRevision,
            PlanBySku = new Dictionary<Guid, decimal> { [sku.Id] = 9, [addedSku.Id] = 2 }
        }));
        var increased = Assert.Single(await stock.ReadSkuAsync(warehouse.Id, sku.Id, default));
        Assert.Equal(9m, increased.AvailableForRoute(assembly.Id));
        Assert.Equal(1m, increased.AvailableQuantity);
    }

    [Fact]
    public async Task Physical_shortage_invalidates_all_reservations_for_the_group_and_assigns_replacements_once()
    {
        var warehouse = new Warehouse { Id = Guid.NewGuid(), Name = "Shared shortage warehouse" };
        var sku = new StockKeepingUnit { Id = Guid.NewGuid(), Name = "Picking SKU" };
        var zone = Zone(warehouse.Id, "PK", ZoneType.Storage);
        var shared = Location(warehouse.Id, zone, 1);
        var replacement = Location(warehouse.Id, zone, 2);
        var (assembly, source) = ActiveAssembly(warehouse.Id, sku.Id, 6);
        var (other, otherSource) = ActiveAssembly(warehouse.Id, sku.Id, 4);
        await using (var seed = database.CreateDbContext())
        {
            seed.AddRange(warehouse, sku, zone, shared, replacement, assembly, source, other, otherSource,
                Balance(warehouse.Id, shared.Id, sku.Id, 10),
                Balance(warehouse.Id, replacement.Id, sku.Id, 4),
                Value(PickingRouteReservation.Create(assembly.Id, warehouse.Id, shared.Id, sku.Id, null, 6)),
                Value(PickingRouteReservation.Create(other.Id, warehouse.Id, shared.Id, sku.Id, null, 4)));
            await seed.SaveChangesAsync();
        }

        var (scan, pick, report) = Commands();
        var scanned = Value(await scan.ExecuteAsync(assembly.Id, sku.Id, shared.Code, Worker));
        Assert.Equal(6m, scanned.SuggestedQuantity);
        Assert.Equal(10m, scanned.ExpectedPhysicalQuantity);
        var request = new ReportPickingShortageRequest(Guid.NewGuid(), assembly.Id, assembly.OperationalRevision,
            shared.Code, sku.Id, null, 10, 3, Worker);
        var shortageId = Value(await report.ExecuteAsync(request));
        Assert.Equal(shortageId, Value(await report.ExecuteAsync(request)));
        Error(await pick.ExecuteAsync(new ConfirmPickRequest(Guid.NewGuid(), other.Id, other.OperationalRevision,
            shared.Code, sku.Id, null, 4, Worker)), OperationErrorType.Conflict);

        await using var verify = database.CreateDbContext();
        var routes = await verify.PickingRouteReservations.Where(x => x.WarehouseId == warehouse.Id).ToArrayAsync();
        Assert.All(routes, row => Assert.Equal(replacement.Id, row.StorageLocationId));
        Assert.Equal(4m, routes.Sum(x => x.Quantity));
        var oldest = new[] { assembly, other }.OrderBy(x => x.StartedAtUtc).ThenBy(x => x.Id).First();
        Assert.Equal(oldest == assembly ? 3m : 4m, routes.Single(x => x.PickingAssemblyId == oldest.Id).Quantity);
        Assert.Equal(3m, (await verify.PickingReservations.SingleAsync(x => x.PickingAssemblyId == assembly.Id)).Quantity);
        Assert.Equal(7m, (await verify.StockShortages.SingleAsync(x => x.Id == shortageId)).BlockedQuantity);
        var adjusted = await verify.PickingAssemblies.SingleAsync(x => x.Id == other.Id);
        Assert.True(adjusted.OperationalRevision > other.OperationalRevision);
    }

    private (GetPickingSource Scan, ConfirmPick Pick, ReportPickingShortage Report) Commands()
    {
        var executor = new CommandExecutor(new CommandExecutionStore(database));
        var assemblies = new PickingAssemblyStore(database);
        var stock = new StockAvailabilityStore(database);
        var routeStock = new AdjustPickingRouteStock(assemblies, stock, new BuildPickingRoute());
        var permission = Permission();
        return (new GetPickingSource(assemblies, stock, permission, TimeProvider.System),
            new ConfirmPick(executor, assemblies, stock, stock, new ReplenishmentStore(database),
                permission, routeStock, TimeProvider.System),
            new ReportPickingShortage(executor, assemblies, stock, stock, stock,
                permission, routeStock, TimeProvider.System));
    }
}

using Microsoft.EntityFrameworkCore;
using Wms.Application.CommandExecution;
using Wms.Application.Inventory;
using Wms.Application.Picking;
using Wms.Application.Replenishment;
using Wms.Common;
using Wms.Data;
using Wms.Domain;
using Wms.Domain.Enums;
using Wms.Tests.Infrastructure;
using static Wms.Tests.Infrastructure.TestSupport;
using static Wms.Tests.Replenishment.ReplenishmentTestScenario;

namespace Wms.Tests.Replenishment;

[Collection(SqlDatabaseCollection.Name)]
public sealed class ReplenishmentLifecycleSafetyTests(SqlDatabase database)
{
    [Fact]
    public async Task Scoped_proposals_keep_mixed_pallets_and_prohibited_destinations_excluded()
    {
        var warehouse = new Warehouse { Id = Guid.NewGuid(), Name = "Scoped replenishment" };
        var sku = new StockKeepingUnit { Id = Guid.NewGuid(), Name = "Requested SKU" };
        var otherSku = new StockKeepingUnit { Id = Guid.NewGuid(), Name = "Unrequested SKU" };
        var reserve = Zone(warehouse.Id, "RES", ZoneType.Reserve);
        var storage = Zone(warehouse.Id, "CMP", ZoneType.Storage);
        var pallet = Location(warehouse.Id, reserve, 1, allowsMultipleSkus: true);
        var prohibited = Location(warehouse.Id, storage, 1);
        var allowed = Location(warehouse.Id, storage, 2, allowsMultipleSkus: true);
        var oldSource = Location(warehouse.Id, reserve, 3);
        var oldDestination = Location(warehouse.Id, storage, 3);
        var (assembly, source) = ActiveAssembly(warehouse.Id, sku.Id, 13);
        var now = DateTimeOffset.UtcNow;
        var date = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30);
        var cancelled = Value(ReplenishmentTask.Order(Guid.NewGuid(), assembly.Id,
            warehouse.Id, sku.Id, 1, oldSource.Id, oldDestination.Id, Worker, now));
        Success(cancelled.Cancel(Worker, now.AddSeconds(1)));
        await using (var seed = database.CreateDbContext())
        {
            seed.AddRange(warehouse, sku, otherSku, reserve, storage, pallet,
                prohibited, allowed, oldSource, oldDestination, assembly, source, cancelled,
                Value(InventoryBalance.Create(Guid.NewGuid(), warehouse.Id,
                    pallet.Id, sku.Id, 4, now, date)),
                Value(InventoryBalance.Create(Guid.NewGuid(), warehouse.Id,
                    pallet.Id, sku.Id, 6, now, date.AddDays(1))),
                Balance(warehouse.Id, pallet.Id, otherSku.Id, 3),
                Value(InventoryBalance.Create(Guid.NewGuid(), warehouse.Id,
                    prohibited.Id, sku.Id, 2, now, date)),
                Value(PickingRouteReservation.Create(assembly.Id, warehouse.Id,
                    prohibited.Id, sku.Id, date, 2)),
                Balance(warehouse.Id, prohibited.Id, otherSku.Id, 2),
                Value(InventoryBalance.Create(Guid.NewGuid(), warehouse.Id,
                    allowed.Id, sku.Id, 1, now, date)),
                Value(PickingRouteReservation.Create(assembly.Id, warehouse.Id,
                    allowed.Id, sku.Id, date, 1)),
                Balance(warehouse.Id, allowed.Id, otherSku.Id, 1));
            await seed.SaveChangesAsync();
        }

        var stock = new StockAvailabilityStore(database);
        var store = new ReplenishmentStore(database);
        var pickingQueries = new PickingAssemblyQueryStore(database);
        var pickingWork = new GetPickingWork(pickingQueries, Permission());
        var query = new GetPickingReplenishments(
            pickingWork,
            new BuildPickingRoute(), stock, store, new BuildReplenishmentProposals(),
            TimeProvider.System);
        var view = Value(await query.ExecuteAsync(assembly.Id, Worker));
        Assert.Equal(3m, view.PickableBySku[sku.Id]);
        Assert.Equal(3m, view.StockBySku[sku.Id].Storage.Quantity);
        var route = Value(await new GetPickingRoute(pickingWork, pickingQueries, stock, new BuildPickingRoute(),
            TimeProvider.System).ExecuteAsync(assembly.Id, Worker));
        Assert.Equal(view.PickableBySku[sku.Id], route.Steps.Sum(x => x.Quantity));
        Assert.Equal(10m, route.Gaps.Sum(x => x.Quantity));
        var proposal = Assert.Single(view.Proposals);
        Assert.Equal(pallet.Id, proposal.SourceLocationId);
        Assert.Equal(allowed.Id, proposal.DestinationLocationId);
        Assert.Equal(10m, proposal.Quantity);
        var history = Assert.Single(view.Tasks);
        Assert.Equal(ReplenishmentTaskStatus.Cancelled, history.Status);
        Assert.Equal(oldSource.Code, history.RecommendedSourceCode);
        Assert.Equal(oldDestination.Code, history.RecommendedDestinationCode);

        var order = new OrderReplenishment(
            new CommandExecutor(new CommandExecutionStore(database)),
            new PickingAssemblyStore(database), store, stock, new BuildPickingRoute(),
            new BuildReplenishmentProposals(), Permission(), TimeProvider.System);
        Value(await order.ExecuteAsync(new(Guid.NewGuid(), assembly.Id,
            view.Revision, sku.Id, Worker)));
        await using var verify = database.CreateDbContext();
        var ordered = await verify.ReplenishmentTasks.SingleAsync(x =>
            x.PickingAssemblyId == assembly.Id && x.Status == ReplenishmentTaskStatus.Ordered);
        Assert.Equal(pallet.Id, ordered.RecommendedSourceStorageLocationId);
        Assert.Equal(allowed.Id, ordered.RecommendedDestinationStorageLocationId);
        Assert.Equal(10m, ordered.OrderedQuantity);
    }

    [Fact]
    public async Task Sku_order_creates_all_source_tasks_once_in_one_commit()
    {
        var warehouse = new Warehouse { Id = Guid.NewGuid(), Name = "SKU replenishment order" };
        var sku = new StockKeepingUnit { Id = Guid.NewGuid(), Name = "Ordered SKU" };
        var reserve = Zone(warehouse.Id, "RES", ZoneType.Reserve);
        var storage = Zone(warehouse.Id, "CMP", ZoneType.Storage);
        var first = Location(warehouse.Id, reserve, 1);
        var second = Location(warehouse.Id, reserve, 2);
        var destination = Location(warehouse.Id, storage, 1);
        var (assembly, source) = ActiveAssembly(warehouse.Id, sku.Id, 7);
        var shortageAssembly = PickingAssembly(warehouse.Id, Worker);
        var revision = assembly.OperationalRevision;
        var expires = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30);
        await using (var seed = database.CreateDbContext())
        {
            seed.AddRange(warehouse, sku, reserve, storage, first, second,
                destination, assembly, source, shortageAssembly,
                Value(InventoryBalance.Create(Guid.NewGuid(), warehouse.Id,
                    first.Id, sku.Id, 4, DateTimeOffset.UtcNow, expires)),
                Value(InventoryBalance.Create(Guid.NewGuid(), warehouse.Id,
                    second.Id, sku.Id, 5, DateTimeOffset.UtcNow, expires)),
                Value(StockShortage.Report(Guid.NewGuid(), warehouse.Id,
                    first.Id, sku.Id, expires, shortageAssembly.Id,
                    4, 3, null, Worker, DateTimeOffset.UtcNow)),
                Value(StockShortage.Report(Guid.NewGuid(), warehouse.Id,
                    second.Id, sku.Id, expires, shortageAssembly.Id,
                    5, 4, null, Worker, DateTimeOffset.UtcNow)));
            await seed.SaveChangesAsync();
        }

        var stock = new StockAvailabilityStore(database);
        var order = new OrderReplenishment(
            new CommandExecutor(new CommandExecutionStore(database)),
            new PickingAssemblyStore(database), new ReplenishmentStore(database),
            stock, new BuildPickingRoute(), new BuildReplenishmentProposals(),
            Permission(), TimeProvider.System);
        var request = new OrderReplenishmentRequest(Guid.NewGuid(), assembly.Id,
            revision, sku.Id, Worker);
        Assert.Equal(assembly.Id, Value(await order.ExecuteAsync(request)));
        Assert.Equal(assembly.Id, Value(await order.ExecuteAsync(request)));
        Error(await order.ExecuteAsync(request with { RequestId = Guid.NewGuid() }),
            OperationErrorType.Conflict);

        await using var verify = database.CreateDbContext();
        var tasks = await verify.ReplenishmentTasks.Where(x =>
            x.PickingAssemblyId == assembly.Id).ToArrayAsync();
        Assert.Equal(2, tasks.Length);
        Assert.Equal(new[] { 3m, 4m }, tasks.Select(x => x.OrderedQuantity)
            .OrderBy(x => x).ToArray());
        Assert.Equal(2, await verify.WarehouseAuditEvents.CountAsync(x =>
            x.PickingAssemblyId == assembly.Id && x.EventType == "replenishment.ordered"));
        Assert.Equal(1, await verify.CommandReceipts.CountAsync(x =>
            x.CommandType == "replenishment.order" && x.RequestId == request.RequestId));
        Assert.Equal(revision + 1, (await verify.PickingAssemblies.SingleAsync(x =>
            x.Id == assembly.Id)).OperationalRevision);
    }

    [Fact]
    public async Task Partial_transfer_can_continue_from_a_later_dated_source_after_assembly_close()
    {
        var seed = await SeedOrderedAsync(8, 4, 10, secondSourceDaysLater: 1);
        var commands = Commands();
        Error(await commands.Start.ExecuteAsync(new(Guid.NewGuid(), seed.TaskId,
            seed.SecondCode, Worker)), OperationErrorType.Conflict);
        var firstCycleId = Value(await commands.Start.ExecuteAsync(
            new(Guid.NewGuid(), seed.TaskId, seed.FirstCode, Worker)));
        Value(await commands.Transfer.ExecuteAsync(new(
            Guid.NewGuid(), seed.TaskId, firstCycleId, seed.DestinationCode,
            seed.SkuId, seed.ExpirationDate, 6, Worker)));
        var completion = new CompleteReplenishmentSourceRequest(Guid.NewGuid(), seed.TaskId,
            firstCycleId, seed.FirstCode, Worker);
        Assert.Equal(firstCycleId, Value(await commands.Complete.ExecuteAsync(completion)));
        Assert.Equal(firstCycleId, Value(await commands.Complete.ExecuteAsync(completion)));

        await using (var close = database.CreateDbContext())
        {
            var assembly = await close.PickingAssemblies.SingleAsync(x =>
                x.Id == seed.AssemblyId);
            Success(assembly.Close(Worker, assembly.OperationalRevision,
                DateTimeOffset.UtcNow));
            await close.SaveChangesAsync();
        }

        var secondCycleId = Value(await commands.Start.ExecuteAsync(
            new(Guid.NewGuid(), seed.TaskId, seed.SecondCode, Worker)));
        Value(await commands.Transfer.ExecuteAsync(new(
            Guid.NewGuid(), seed.TaskId, secondCycleId,
            seed.DestinationCode, seed.SkuId, seed.ExpirationDate.AddDays(1), 4, Worker)));
        Value(await commands.Complete.ExecuteAsync(new(
            Guid.NewGuid(), seed.TaskId, secondCycleId, seed.SecondCode, Worker)));

        await using var verify = database.CreateDbContext();
        var task = await verify.ReplenishmentTasks.Include(x => x.SourceCycles)
            .ThenInclude(x => x.Transfers).SingleAsync(x => x.Id == seed.TaskId);
        Assert.Equal(ReplenishmentTaskStatus.Completed, task.Status);
        Assert.Equal(10m, task.TransferredQuantity);
        Assert.Equal(2, task.SourceCycles.Count);
        Assert.All(task.SourceCycles, x => Assert.NotNull(x.CompletedAtUtc));
        Assert.False(await verify.StorageLocationLocks.AnyAsync(x =>
            x.OwnerId == seed.TaskId));
        Assert.False(await verify.StockShortages.AnyAsync(x => x.WarehouseId == seed.WarehouseId));
        Assert.Equal(2m, (await verify.InventoryBalances.SingleAsync(x =>
            x.StorageLocationId == seed.FirstId && x.StockKeepingUnitId == seed.SkuId)).Quantity);
        Assert.Equal(10m, await verify.InventoryBalances.Where(x =>
            x.StorageLocationId == seed.DestinationId).SumAsync(x => x.Quantity));
        Assert.False(await verify.PickingRouteReservations.AnyAsync(x =>
            x.PickingAssemblyId == seed.AssemblyId && x.ExpirationDate == seed.ExpirationDate.AddDays(1)));
    }

    [Fact]
    public async Task Cancellation_and_source_scan_preserve_the_winning_commit_in_both_orders()
    {
        var cancelledFirst = await SeedOrderedAsync(4, 0, 4);
        var commands = Commands();
        var cancelled = new CancelPickingAssembly(
            new CommandExecutor(new CommandExecutionStore(database)),
            new PickingAssemblyStore(database), Permission(), TimeProvider.System);
        Value(await cancelled.ExecuteAsync(new(Guid.NewGuid(),
            cancelledFirst.AssemblyId, cancelledFirst.AssemblyRevision,
            Manager, "Сборка отменена до скана")));
        Error(await commands.Start.ExecuteAsync(new(Guid.NewGuid(),
            cancelledFirst.TaskId, cancelledFirst.FirstCode, Worker)),
            OperationErrorType.Conflict);

        var startedFirst = await SeedOrderedWithPickingStockAsync();
        await using (var reserve = database.CreateDbContext())
        {
            reserve.Add(Value(PickingRouteReservation.Create(startedFirst.AssemblyId, startedFirst.WarehouseId,
                startedFirst.DestinationId, startedFirst.SkuId, startedFirst.ExpirationDate, 4)));
            await reserve.SaveChangesAsync();
        }
        Value(await commands.Start.ExecuteAsync(new(Guid.NewGuid(),
            startedFirst.TaskId, startedFirst.FirstCode, Worker)));
        var cancelAfterScan = new CancelPickingAssemblyRequest(Guid.NewGuid(),
            startedFirst.AssemblyId, startedFirst.AssemblyRevision,
            Manager, "Сборка отменена после скана");
        Value(await cancelled.ExecuteAsync(cancelAfterScan));
        Value(await cancelled.ExecuteAsync(cancelAfterScan));

        await using var verify = database.CreateDbContext();
        Assert.Equal(ReplenishmentTaskStatus.Cancelled,
            (await verify.ReplenishmentTasks.SingleAsync(x =>
                x.Id == cancelledFirst.TaskId)).Status);
        Assert.False(await verify.StorageLocationLocks.AnyAsync(x =>
            x.OwnerId == cancelledFirst.TaskId));
        Assert.Equal(ReplenishmentTaskStatus.InProgress,
            (await verify.ReplenishmentTasks.SingleAsync(x =>
                x.Id == startedFirst.TaskId)).Status);
        Assert.Equal(PickingAssemblyStatus.CancellationPending,
            (await verify.PickingAssemblies.SingleAsync(x =>
                x.Id == startedFirst.AssemblyId)).Status);
        Assert.True(await verify.StorageLocationLocks.AnyAsync(x =>
            x.OwnerId == startedFirst.TaskId));
        var stock = await new StockAvailabilityStore(database)
            .ReadSkuAsync(startedFirst.WarehouseId, startedFirst.SkuId, default);
        var destination = stock.Single(x => x.Group.StorageLocationId == startedFirst.DestinationId);
        Assert.Equal(4m, destination.AvailableQuantity);
        Assert.Equal(0m, destination.RouteReservedFor(startedFirst.AssemblyId));
    }

    [Fact]
    public async Task Pick_that_closes_demand_cancels_only_an_unstarted_replenishment()
    {
        var executor = new CommandExecutor(new CommandExecutionStore(database));
        var assemblies = new PickingAssemblyStore(database);
        var replenishments = new ReplenishmentStore(database);
        var stock = new StockAvailabilityStore(database);
        var pick = new ConfirmPick(executor, assemblies, stock, stock,
            replenishments, Permission(), new AdjustPickingRouteStock(assemblies, stock, new BuildPickingRoute()),
            TimeProvider.System);
        var start = Commands().Start;

        var cancelledFirst = await SeedOrderedWithPickingStockAsync();
        var request = new ConfirmPickRequest(Guid.NewGuid(),
            cancelledFirst.AssemblyId, cancelledFirst.AssemblyRevision,
            cancelledFirst.DestinationCode, cancelledFirst.SkuId,
            cancelledFirst.ExpirationDate, 4, Worker);
        Value(await pick.ExecuteAsync(request));
        Value(await pick.ExecuteAsync(request));
        Error(await start.ExecuteAsync(new(Guid.NewGuid(), cancelledFirst.TaskId,
            cancelledFirst.FirstCode, Worker)), OperationErrorType.Conflict);

        var startedFirst = await SeedOrderedWithPickingStockAsync();
        Value(await start.ExecuteAsync(new(Guid.NewGuid(), startedFirst.TaskId,
            startedFirst.FirstCode, Worker)));
        Value(await pick.ExecuteAsync(new(Guid.NewGuid(), startedFirst.AssemblyId,
            startedFirst.AssemblyRevision, startedFirst.DestinationCode,
            startedFirst.SkuId, startedFirst.ExpirationDate, 4, Worker)));

        await using var verify = database.CreateDbContext();
        Assert.Equal(ReplenishmentTaskStatus.Cancelled,
            (await verify.ReplenishmentTasks.SingleAsync(x =>
                x.Id == cancelledFirst.TaskId)).Status);
        Assert.Equal(ReplenishmentTaskStatus.InProgress,
            (await verify.ReplenishmentTasks.SingleAsync(x =>
                x.Id == startedFirst.TaskId)).Status);
        Assert.False(await verify.StorageLocationLocks.AnyAsync(x =>
            x.OwnerId == cancelledFirst.TaskId));
        Assert.True(await verify.StorageLocationLocks.AnyAsync(x =>
            x.OwnerId == startedFirst.TaskId));
        Assert.Equal(1, await verify.PickingEntries.CountAsync(x =>
            x.PickingAssemblyId == cancelledFirst.AssemblyId));
        Assert.Empty(await verify.InventoryMovements.Where(x =>
            x.RecorderId == cancelledFirst.AssemblyId).ToArrayAsync());
        Assert.Equal(1, await verify.WarehouseAuditEvents.CountAsync(x =>
            x.PickingAssemblyId == cancelledFirst.AssemblyId
            && x.EventType == "replenishment.cancelled.unneeded"));
    }

    private (StartReplenishmentSource Start,
        ConfirmReplenishmentTransfer Transfer,
        CompleteReplenishmentSource Complete) Commands()
    {
        var executor = new CommandExecutor(new CommandExecutionStore(database));
        var replenishments = new ReplenishmentStore(database);
        var stock = new StockAvailabilityStore(database);
        var routeStock = new AdjustPickingRouteStock(new PickingAssemblyStore(database), stock, new BuildPickingRoute());
        var permission = Permission();
        return (new StartReplenishmentSource(executor, replenishments,
                stock, stock, permission, TimeProvider.System),
            new ConfirmReplenishmentTransfer(executor, replenishments,
                stock, new PostInventoryMovement.Handler(new InventoryPostingStore()),
                permission, routeStock, TimeProvider.System),
            new CompleteReplenishmentSource(executor, replenishments,
                permission, TimeProvider.System));
    }

    private async Task<Seed> SeedOrderedAsync(decimal firstQuantity,
        decimal secondQuantity, decimal orderedQuantity, int secondSourceDaysLater = 0)
    {
        var warehouse = new Warehouse { Id = Guid.NewGuid(), Name = "Replenishment lifecycle" };
        var sku = new StockKeepingUnit { Id = Guid.NewGuid(), Name = "Replenished SKU" };
        var reserve = Zone(warehouse.Id, "RES", ZoneType.Reserve);
        var storage = Zone(warehouse.Id, "CMP", ZoneType.Storage);
        var first = Location(warehouse.Id, reserve, 1);
        var second = Location(warehouse.Id, reserve, 2);
        var destination = Location(warehouse.Id, storage, 1);
        var date = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30);
        var now = DateTimeOffset.UtcNow;
        var (assembly, shippingSource) = ActiveAssembly(warehouse.Id,
            sku.Id, orderedQuantity);
        var task = Value(ReplenishmentTask.Order(Guid.NewGuid(), assembly.Id,
            warehouse.Id, sku.Id, orderedQuantity, first.Id, destination.Id,
            Worker, now));
        await using var db = database.CreateDbContext();
        db.AddRange(warehouse, sku, reserve, storage, first, second,
            destination, assembly, shippingSource, task,
            Value(InventoryBalance.Create(Guid.NewGuid(), warehouse.Id,
                first.Id, sku.Id, firstQuantity, now, date)));
        if (secondQuantity > 0)
            db.Add(Value(InventoryBalance.Create(Guid.NewGuid(), warehouse.Id,
                second.Id, sku.Id, secondQuantity, now, date.AddDays(secondSourceDaysLater))));
        await db.SaveChangesAsync();
        return new Seed(warehouse.Id, assembly.Id, assembly.OperationalRevision,
            task.Id, sku.Id, first.Id, first.Code, second.Code,
            destination.Id, destination.Code, date);
    }

    private async Task<Seed> SeedOrderedWithPickingStockAsync()
    {
        var seed = await SeedOrderedAsync(4, 0, 4);
        await using var db = database.CreateDbContext();
        db.Add(Value(InventoryBalance.Create(Guid.NewGuid(), seed.WarehouseId,
            seed.DestinationId, seed.SkuId, 4, DateTimeOffset.UtcNow,
            seed.ExpirationDate)));
        await db.SaveChangesAsync();
        return seed;
    }

    private sealed record Seed(Guid WarehouseId, Guid AssemblyId,
        long AssemblyRevision, Guid TaskId, Guid SkuId,
        Guid FirstId, string FirstCode, string SecondCode,
        Guid DestinationId, string DestinationCode, DateOnly ExpirationDate);
}

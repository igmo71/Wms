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
    public async Task Shortage_block_survives_empty_source_and_second_source_finishes_after_assembly_close()
    {
        var seed = await SeedOrderedAsync(8, 4, 10);
        var commands = Commands();
        var firstCycleId = Value(await commands.Start.ExecuteAsync(
            new(Guid.NewGuid(), seed.TaskId, seed.FirstCode, Worker)));
        Value(await commands.Shortage.ExecuteAsync(new(
            Guid.NewGuid(), seed.TaskId, firstCycleId, seed.SkuId,
            seed.ExpirationDate, 8, 6, Worker)));
        Value(await commands.Transfer.ExecuteAsync(new(
            Guid.NewGuid(), seed.TaskId, firstCycleId, seed.DestinationCode,
            seed.SkuId, seed.ExpirationDate, 6, Worker)));
        Value(await commands.Complete.ExecuteAsync(new(
            Guid.NewGuid(), seed.TaskId, firstCycleId, seed.FirstCode,
            true, Worker)));

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
            seed.DestinationCode, seed.SkuId, seed.ExpirationDate, 4, Worker)));
        Value(await commands.Complete.ExecuteAsync(new(
            Guid.NewGuid(), seed.TaskId, secondCycleId, seed.SecondCode,
            true, Worker)));

        await using var verify = database.CreateDbContext();
        var task = await verify.ReplenishmentTasks.Include(x => x.SourceCycles)
            .ThenInclude(x => x.Transfers).SingleAsync(x => x.Id == seed.TaskId);
        Assert.Equal(ReplenishmentTaskStatus.Completed, task.Status);
        Assert.Equal(10m, task.TransferredQuantity);
        Assert.Equal(2, task.SourceCycles.Count);
        Assert.All(task.SourceCycles, x => Assert.NotNull(x.CompletedAtUtc));
        Assert.False(await verify.StorageLocationLocks.AnyAsync(x =>
            x.OwnerId == seed.TaskId));
        var blocked = await verify.StockShortages.SingleAsync(x =>
            x.WarehouseId == seed.WarehouseId && x.StorageLocationId == seed.FirstId);
        Assert.Null(blocked.ResolvedAtUtc);
        Assert.Equal(2m, blocked.BlockedQuantity);
        Assert.Equal(2m, (await verify.InventoryBalances.SingleAsync(x =>
            x.StorageLocationId == seed.FirstId && x.StockKeepingUnitId == seed.SkuId)).Quantity);
        Assert.Equal(10m, (await verify.InventoryBalances.SingleAsync(x =>
            x.StorageLocationId == seed.DestinationId)).Quantity);
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

        var startedFirst = await SeedOrderedAsync(4, 0, 4);
        Value(await commands.Start.ExecuteAsync(new(Guid.NewGuid(),
            startedFirst.TaskId, startedFirst.FirstCode, Worker)));
        Value(await cancelled.ExecuteAsync(new(Guid.NewGuid(),
            startedFirst.AssemblyId, startedFirst.AssemblyRevision,
            Manager, "Сборка отменена после скана")));

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
    }

    private (StartReplenishmentSource Start,
        ConfirmReplenishmentTransfer Transfer,
        ReportReplenishmentShortage Shortage,
        CompleteReplenishmentSource Complete) Commands()
    {
        var executor = new CommandExecutor(new CommandExecutionStore(database));
        var replenishments = new ReplenishmentStore(database);
        var stock = new StockAvailabilityStore(database);
        var permission = Permission();
        return (new StartReplenishmentSource(executor, replenishments,
                stock, stock, permission, TimeProvider.System),
            new ConfirmReplenishmentTransfer(executor, replenishments,
                stock, new PostInventoryMovement.Handler(new InventoryPostingStore()),
                permission, TimeProvider.System),
            new ReportReplenishmentShortage(executor, replenishments,
                stock, stock, permission, TimeProvider.System),
            new CompleteReplenishmentSource(executor, replenishments,
                permission, TimeProvider.System));
    }

    private async Task<Seed> SeedOrderedAsync(decimal firstQuantity,
        decimal secondQuantity, decimal orderedQuantity)
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
                second.Id, sku.Id, secondQuantity, now, date)));
        await db.SaveChangesAsync();
        return new Seed(warehouse.Id, assembly.Id, assembly.OperationalRevision,
            task.Id, sku.Id, first.Id, first.Code, second.Code,
            destination.Id, destination.Code, date);
    }

    private sealed record Seed(Guid WarehouseId, Guid AssemblyId,
        long AssemblyRevision, Guid TaskId, Guid SkuId,
        Guid FirstId, string FirstCode, string SecondCode,
        Guid DestinationId, string DestinationCode, DateOnly ExpirationDate);
}

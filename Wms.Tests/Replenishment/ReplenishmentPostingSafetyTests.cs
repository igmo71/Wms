using Microsoft.EntityFrameworkCore;
using Wms.Application.CommandExecution;
using Wms.Application.Inventory;
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
public sealed class ReplenishmentPostingSafetyTests(SqlDatabase database)
{
    [Fact]
    public async Task Failed_post_rolls_back_progress_and_replayed_dated_transfers_post_once()
    {
        var warehouse = new Warehouse { Id = Guid.NewGuid(), Name = "Replenishment posting" };
        var sku = new StockKeepingUnit { Id = Guid.NewGuid(), Name = "Requested SKU" };
        var otherSku = new StockKeepingUnit { Id = Guid.NewGuid(), Name = "Other pallet SKU" };
        var reserve = Zone(warehouse.Id, "RES", ZoneType.Reserve);
        var storage = Zone(warehouse.Id, "CMP", ZoneType.Storage);
        var source = Location(warehouse.Id, reserve, 1, allowsMultipleSkus: true);
        var fullDestination = Location(warehouse.Id, storage, 1);
        var destination = Location(warehouse.Id, storage, 2);
        var firstDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30);
        var secondDate = firstDate.AddDays(30);
        var now = DateTimeOffset.UtcNow;
        var (assembly, shippingSource) = ActiveAssembly(warehouse.Id, sku.Id, 10);
        var task = Value(ReplenishmentTask.Order(Guid.NewGuid(), assembly.Id,
            warehouse.Id, sku.Id, 10, source.Id, destination.Id, Worker, now));
        var cycle = Value(task.StartSource(Guid.NewGuid(), source.Id, Worker, now));
        await using (var seed = database.CreateDbContext())
        {
            seed.AddRange(warehouse, sku, otherSku, reserve, storage, source,
                fullDestination, destination, assembly, shippingSource, task,
                Value(StorageLocationLock.CreateForReplenishment(source.Id,
                    task.Id, now, Worker)),
                Value(InventoryBalance.Create(Guid.NewGuid(), warehouse.Id,
                    source.Id, sku.Id, 4, now, firstDate)),
                Value(InventoryBalance.Create(Guid.NewGuid(), warehouse.Id,
                    source.Id, sku.Id, 6, now, secondDate)),
                Value(InventoryBalance.Create(Guid.NewGuid(), warehouse.Id,
                    source.Id, otherSku.Id, 3, now)),
                Value(InventoryBalance.Create(Guid.NewGuid(), warehouse.Id,
                    fullDestination.Id, sku.Id, WarehouseQuantity.Maximum,
                    now, firstDate)));
            await seed.SaveChangesAsync();
        }

        var transfer = new ConfirmReplenishmentTransfer(
            new CommandExecutor(new CommandExecutionStore(database)),
            new ReplenishmentStore(database), new StockAvailabilityStore(database),
            new PostInventoryMovement.Handler(new InventoryPostingStore()),
            Permission(), TimeProvider.System);
        var rejectedId = Guid.NewGuid();
        var rejected = await transfer.ExecuteAsync(new ConfirmReplenishmentTransferRequest(
            rejectedId, task.Id, cycle.Id, fullDestination.Code, sku.Id,
            firstDate, 4, Worker));
        Assert.False(rejected.IsSuccess);

        await using (var verify = database.CreateDbContext())
        {
            var unchanged = await verify.ReplenishmentTasks.Include(x => x.SourceCycles)
                .ThenInclude(x => x.Transfers).SingleAsync(x => x.Id == task.Id);
            Assert.Equal(0m, unchanged.TransferredQuantity);
            Assert.Empty(Assert.Single(unchanged.SourceCycles).Transfers);
            Assert.False(await verify.InventoryMovements.AnyAsync(x =>
                x.RecorderId == task.Id));
            Assert.False(await verify.CommandReceipts.AnyAsync(x =>
                x.RequestId == rejectedId));
        }

        var firstRequest = new ConfirmReplenishmentTransferRequest(Guid.NewGuid(),
            task.Id, cycle.Id, destination.Code, sku.Id, firstDate, 4, Worker);
        Value(await transfer.ExecuteAsync(firstRequest));
        Value(await transfer.ExecuteAsync(firstRequest));
        Value(await transfer.ExecuteAsync(firstRequest with
        {
            RequestId = Guid.NewGuid(), ExpirationDate = secondDate, Quantity = 6
        }));

        await using var final = database.CreateDbContext();
        var posted = await final.ReplenishmentTasks.Include(x => x.SourceCycles)
            .ThenInclude(x => x.Transfers).SingleAsync(x => x.Id == task.Id);
        Assert.Equal(10m, posted.TransferredQuantity);
        Assert.Equal(ReplenishmentTaskStatus.InProgress, posted.Status);
        Assert.Equal(2, Assert.Single(posted.SourceCycles).Transfers.Count);
        Assert.Equal(2, await final.InventoryMovements.CountAsync(x =>
            x.RecorderId == task.Id && x.RecorderType == RecorderType.ReplenishmentTask));
        Assert.Equal(4, await final.InventoryTurnovers.CountAsync(x =>
            x.InventoryMovement!.RecorderId == task.Id));
        Assert.Equal(2, await final.CommandReceipts.CountAsync(x =>
            x.CommandType == "replenishment.transfer.confirm"
            && x.UserId == Worker));
        Assert.Equal(3m, (await final.InventoryBalances.SingleAsync(x =>
            x.StorageLocationId == source.Id && x.StockKeepingUnitId == otherSku.Id)).Quantity);
        Assert.Equal(4m, (await final.InventoryBalances.SingleAsync(x =>
            x.StorageLocationId == destination.Id && x.ExpirationDate == firstDate)).Quantity);
        Assert.Equal(6m, (await final.InventoryBalances.SingleAsync(x =>
            x.StorageLocationId == destination.Id && x.ExpirationDate == secondDate)).Quantity);
        Assert.True(await final.StorageLocationLocks.AnyAsync(x =>
            x.StorageLocationId == source.Id && x.OwnerId == task.Id));
    }
}

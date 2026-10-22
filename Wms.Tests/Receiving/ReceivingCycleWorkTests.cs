using Microsoft.EntityFrameworkCore;
using Wms.Application.Authorization;
using Wms.Application.CommandExecution;
using Wms.Application.Receiving;
using Wms.Data;
using Wms.Domain;
using Wms.Domain.Enums;
using Wms.Tests.Infrastructure;
using static Wms.Tests.Infrastructure.TestSupport;

namespace Wms.Tests.Receiving;

[Collection(SqlDatabaseCollection.Name)]
public sealed class ReceivingCycleWorkTests(SqlDatabase database)
{
    [Fact]
    public async Task Clearing_open_lpn_removes_saved_item_once_and_rejects_stale_revision()
    {
        var warehouseId = Guid.NewGuid();
        var skuId = Guid.NewGuid();
        var zone = Zone(warehouseId, "RC", ZoneType.Receiving);
        var location = Location(warehouseId, zone, 1);
        var now = DateTimeOffset.UtcNow;
        var order = Value(ReceivingOrder.CreateNew(warehouseId, now));
        AssignTestReceivingOrderNumber(order);
        Value(order.JoinCurrentCycle(location.Id, "operator-test", now));
        var batch = Value(LicensePlateNumberBatch.Issue(1, "operator-test", now));
        await using (var seed = database.CreateDbContext())
        {
            seed.Warehouses.Add(new Warehouse { Id = warehouseId, Name = "Receiving work" });
            seed.Zones.Add(zone);
            seed.StorageLocations.Add(location);
            seed.StockKeepingUnits.Add(new StockKeepingUnit { Id = skuId, Name = "SKU" });
            seed.ReceivingOrders.Add(order);
            seed.ReceivingOrderItems.Add(Value(
                ReceivingOrderItem.CreateForSku(order.Id, skuId, 4m)));
            seed.LicensePlateNumberBatches.Add(batch);
            await seed.SaveChangesAsync();
        }

        var store = new ReceivingWorkStore(database);
        var executor = new CommandExecutor(new CommandExecutionStore(database));
        var permission = new WarehousePermission(new ReceivingPermission());
        var open = new OpenReceivingLpn(executor, store, permission, TimeProvider.System);
        var edit = new EditReceivingLpn(executor, store, permission, TimeProvider.System);
        var clear = new ClearReceivingLpnItem(executor, store, permission, TimeProvider.System);
        var lpnId = Value(await open.ExecuteAsync(new OpenReceivingLpnRequest(
            Guid.NewGuid(), order.Id, batch.Labels.Single().Code, "operator-test")));
        Value(await edit.ExecuteAsync(new EditReceivingLpnRequest(
            Guid.NewGuid(), order.Id, lpnId, skuId, 2m, null, "operator-test")));

        long revision;
        await using (var read = database.CreateDbContext())
            revision = await read.LicensePlateNumbers.AsNoTracking()
                .Where(x => x.Id == lpnId).Select(x => x.OperationalRevision).SingleAsync();

        var request = new ClearReceivingLpnItemRequest(
            Guid.NewGuid(), order.Id, lpnId, revision, "operator-test");
        Assert.Equal(lpnId, Value(await clear.ExecuteAsync(request)));
        Assert.Equal(lpnId, Value(await clear.ExecuteAsync(request)));
        Value(await edit.ExecuteAsync(new EditReceivingLpnRequest(
            Guid.NewGuid(), order.Id, lpnId, skuId, 3m, null, "operator-test")));
        Assert.False((await clear.ExecuteAsync(request with { RequestId = Guid.NewGuid() })).IsSuccess);

        await using var verify = database.CreateDbContext();
        var saved = await verify.LicensePlateNumbers.Include(x => x.Items)
            .SingleAsync(x => x.Id == lpnId);
        Assert.Equal(LicensePlateNumberStatus.Open, saved.Status);
        Assert.Equal(2m, Assert.Single(saved.Items.Where(x => x.IsDeleted)).Quantity);
        Assert.Equal(3m, Assert.Single(saved.Items.Where(x => !x.IsDeleted)).Quantity);
        Assert.Empty(await verify.InventoryMovements.Where(x => x.LicensePlateNumberId == lpnId)
            .ToListAsync());
        Assert.Equal(1, await verify.CommandReceipts.CountAsync(x => x.RequestId == request.RequestId));
        Assert.Equal(1, await verify.WarehouseAuditEvents.CountAsync(x =>
            x.ReceivingOrderId == order.Id && x.CommandType == "receiving.lpn.item.clear"));
    }

    [Fact]
    public async Task Edited_lpn_cancels_without_stock_and_then_allows_repeat_safe_zero_check()
    {
        var warehouseId = Guid.NewGuid();
        var skuId = Guid.NewGuid();
        var zone = Zone(warehouseId, "RC", ZoneType.Receiving);
        var location = Location(warehouseId, zone, 1);
        var now = DateTimeOffset.UtcNow;
        var order = Value(ReceivingOrder.CreateNew(warehouseId, now));
        AssignTestReceivingOrderNumber(order);
        Value(order.JoinCurrentCycle(location.Id, "operator-test", now));
        var batch = Value(LicensePlateNumberBatch.Issue(1, "operator-test", now));
        await using (var seed = database.CreateDbContext())
        {
            seed.Warehouses.Add(new Warehouse { Id = warehouseId, Name = "Receiving work" });
            seed.Zones.Add(zone);
            seed.StorageLocations.Add(location);
            seed.StockKeepingUnits.Add(new StockKeepingUnit { Id = skuId, Name = "SKU" });
            seed.ReceivingOrders.Add(order);
            seed.ReceivingOrderItems.Add(Value(
                ReceivingOrderItem.CreateForSku(order.Id, skuId, 4m)));
            seed.LicensePlateNumberBatches.Add(batch);
            await seed.SaveChangesAsync();
        }

        var store = new ReceivingWorkStore(database);
        var executor = new CommandExecutor(new CommandExecutionStore(database));
        var permission = new WarehousePermission(new ReceivingPermission());
        var open = new OpenReceivingLpn(executor, store, permission, TimeProvider.System);
        var edit = new EditReceivingLpn(executor, store, permission, TimeProvider.System);
        var cancel = new CancelReceivingLpn(executor, store, permission, TimeProvider.System);
        var zero = new ConfirmZeroReceived(executor, store, permission, TimeProvider.System);

        var openRequest = new OpenReceivingLpnRequest(
            Guid.NewGuid(), order.Id, batch.Labels.Single().Code, "operator-test");
        var lpnId = Value(await open.ExecuteAsync(openRequest));
        Assert.Equal(lpnId, Value(await open.ExecuteAsync(openRequest)));
        var editRequest = new EditReceivingLpnRequest(
            Guid.NewGuid(), order.Id, lpnId, skuId, 2m, null, "operator-test");
        Assert.Equal(lpnId, Value(await edit.ExecuteAsync(editRequest)));
        Assert.Equal(lpnId, Value(await edit.ExecuteAsync(editRequest)));
        Assert.False((await zero.ExecuteAsync(new ConfirmZeroReceivedRequest(
            Guid.NewGuid(), order.Id, skuId, "operator-test"))).IsSuccess);
        var cancelRequest = new CancelReceivingLpnRequest(
            Guid.NewGuid(), order.Id, lpnId, "operator-test");
        Assert.Equal(lpnId, Value(await cancel.ExecuteAsync(cancelRequest)));
        var zeroRequest = new ConfirmZeroReceivedRequest(
            Guid.NewGuid(), order.Id, skuId, "operator-test");
        Assert.Equal(skuId, Value(await zero.ExecuteAsync(zeroRequest)));
        Assert.Equal(skuId, Value(await zero.ExecuteAsync(zeroRequest)));

        await using var verify = database.CreateDbContext();
        Assert.Equal(0m, (await verify.ReceivingOrderItems.SingleAsync(x =>
            x.ReceivingOrderId == order.Id && x.StockKeepingUnitId == skuId)).FactQuantity);
        var lpn = await verify.LicensePlateNumbers.Include(x => x.Items)
            .SingleAsync(x => x.Id == lpnId);
        Assert.Equal(LicensePlateNumberStatus.Cancelled, lpn.Status);
        Assert.Equal(2m, Assert.Single(lpn.Items).Quantity);
        Assert.Empty(await verify.InventoryMovements.Where(x =>
            x.LicensePlateNumberId == lpnId).ToListAsync());
        var requestIds = new[]
            { openRequest.RequestId, editRequest.RequestId, cancelRequest.RequestId,
                zeroRequest.RequestId };
        Assert.Equal(4, await verify.CommandReceipts.CountAsync(x =>
            requestIds.Contains(x.RequestId)));
        Assert.Equal(4, await verify.WarehouseAuditEvents.CountAsync(x =>
            x.ReceivingOrderId == order.Id));
    }

    private sealed class ReceivingPermission : IWarehousePermissionStore
    {
        public Task<bool> HasManagerRoleAsync(string userId, CancellationToken ct) =>
            Task.FromResult(false);

        public Task<bool> HasReceivingRoleAsync(string userId, CancellationToken ct) =>
            Task.FromResult(userId == "operator-test");
    }
}

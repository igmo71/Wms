using Microsoft.EntityFrameworkCore;
using Wms.Application.Authorization;
using Wms.Application.CommandExecution;
using Wms.Application.Receiving;
using Wms.Data;
using Wms.Domain;
using Wms.Tests.Infrastructure;
using static Wms.Tests.Infrastructure.TestSupport;

namespace Wms.Tests.Receiving;

[Collection(SqlDatabaseCollection.Name)]
public sealed class ReceivingSkuSelectionTests(SqlDatabase database)
{
    [Fact]
    public async Task Catalog_selection_adds_zero_plan_once_and_barcode_scans_globally()
    {
        var warehouseId = Guid.NewGuid();
        var plannedSkuId = Guid.NewGuid();
        var manualSkuId = Guid.NewGuid();
        var scannedSkuId = Guid.NewGuid();
        var order = Value(ReceivingOrder.CreateNew(warehouseId, DateTimeOffset.UtcNow));
        await using (var seed = database.CreateDbContext())
        {
            seed.Warehouses.Add(new Warehouse { Id = warehouseId, Name = "SKU selection" });
            seed.StockKeepingUnits.AddRange(
                new StockKeepingUnit { Id = plannedSkuId, Name = "Planned item" },
                new StockKeepingUnit { Id = manualSkuId, Name = "Manual item without barcode" },
                new StockKeepingUnit { Id = scannedSkuId, Name = "Scanned item" });
            seed.SkuBarcodes.Add(new SkuBarcode { SkuId = scannedSkuId, Value = "SCAN-OUTSIDE" });
            seed.ReceivingOrders.Add(order);
            seed.ReceivingOrderItems.Add(Value(
                ReceivingOrderItem.CreateForSku(order.Id, plannedSkuId, 5m)));
            await seed.SaveChangesAsync();
        }

        var store = new ReceivingSkuStore(database);
        var search = new SearchReceivingSku(store);
        Assert.Empty(Value(await search.SearchAsync(
            order.Id, "Manual", ReceivingSkuSearchScope.Order)));
        var catalog = Value(await search.SearchAsync(
            order.Id, "Manual", ReceivingSkuSearchScope.Catalog));
        Assert.Equal(manualSkuId, Assert.Single(catalog).SkuId);
        Assert.False(catalog[0].IsInOrder);
        var scanned = Value(await search.ResolveBarcodeAsync(order.Id, "SCAN-OUTSIDE"));
        Assert.Equal(scannedSkuId, scanned.SkuId);
        Assert.False(scanned.IsInOrder);

        var add = new AddReceivingOrderSku(
            new CommandExecutor(new CommandExecutionStore(database)), store,
            new WarehousePermission(new ReceivingPermission()), TimeProvider.System);
        var request = new AddReceivingOrderSkuRequest(
            Guid.NewGuid(), order.Id, manualSkuId, "operator-test");
        Assert.Equal(manualSkuId, Value(await add.ExecuteAsync(request)));
        Assert.Equal(manualSkuId, Value(await add.ExecuteAsync(request)));
        Assert.Equal(manualSkuId, Value(await add.ExecuteAsync(
            request with { RequestId = Guid.NewGuid() })));

        await using var verify = database.CreateDbContext();
        var item = await verify.ReceivingOrderItems.SingleAsync(x =>
            x.ReceivingOrderId == order.Id && x.StockKeepingUnitId == manualSkuId);
        Assert.Equal(0m, item.CurrentPlanQuantity);
        Assert.Null(item.FactQuantity);
        Assert.Equal(1, await verify.WarehouseAuditEvents.CountAsync(x =>
            x.ReceivingOrderId == order.Id && x.StockKeepingUnitId == manualSkuId
            && x.EventType == "receiving.order.sku-added"));
        Assert.Equal(2, await verify.CommandReceipts.CountAsync(x =>
            x.CommandType == "receiving.order.add-sku"));
        var inOrder = Value(await search.SearchAsync(
            order.Id, "Manual", ReceivingSkuSearchScope.Order));
        Assert.True(Assert.Single(inOrder).IsInOrder);
    }

    private sealed class ReceivingPermission : IWarehousePermissionStore
    {
        public Task<bool> HasManagerRoleAsync(string userId, CancellationToken ct) =>
            Task.FromResult(false);

        public Task<bool> HasReceivingRoleAsync(string userId, CancellationToken ct) =>
            Task.FromResult(userId == "operator-test");
    }
}

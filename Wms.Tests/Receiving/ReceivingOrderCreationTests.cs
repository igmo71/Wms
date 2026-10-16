using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Wms.Application.Authorization;
using Wms.Application.CommandExecution;
using Wms.Application.Receiving;
using Wms.Common;
using Wms.Data;
using Wms.Domain;
using Wms.Domain.Enums;
using Wms.Integration.OneS;
using Wms.Tests.Infrastructure;
using static Wms.Tests.Infrastructure.TestSupport;

namespace Wms.Tests.Receiving;

[Collection(SqlDatabaseCollection.Name)]
public sealed class ReceivingOrderCreationTests(SqlDatabase database)
{
    [Fact]
    public async Task Completing_and_reopening_keeps_old_cycle_snapshot_and_posts_no_stock()
    {
        var warehouseId = Guid.NewGuid();
        var skuId = Guid.NewGuid();
        var sourceKey = Guid.NewGuid();
        var zone = Zone(warehouseId, "RC", ZoneType.Receiving);
        var firstLocation = Location(warehouseId, zone, 1);
        var secondLocation = Location(warehouseId, zone, 2);
        await using (var seed = database.CreateDbContext())
        {
            seed.Warehouses.Add(new Warehouse { Id = warehouseId, Name = "Receiving cycles" });
            seed.Zones.Add(zone);
            seed.StorageLocations.AddRange(firstLocation, secondLocation);
            seed.StockKeepingUnits.Add(new StockKeepingUnit { Id = skuId, Name = "Cycle SKU" });
            await seed.SaveChangesAsync();
        }

        var responses = new Dictionary<Guid, string>
        {
            [sourceKey] = Source(sourceKey, warehouseId, $$"""
                [{"LineNumber":"1","Номенклатура_Key":"{{skuId}}","Количество":2}]
                """)
        };
        var parser = new ReceivingSourceParser();
        var synchronize = new SynchronizeReceivingSource(
            new StubReader(responses), parser,
            new ReceivingSourceStore(database, parser), TimeProvider.System);
        Assert.True((await synchronize.ExecuteAsync(sourceKey)).IsSuccess);
        var orderId = Value(await new CreateReceivingOrder(
            new ReceivingOrderStore(database), parser, TimeProvider.System)
            .ExecuteAsync([sourceKey]));
        var executor = new CommandExecutor(new CommandExecutionStore(database));
        var store = new ReceivingWorkStore(database);
        var permission = new WarehousePermission(new ManagerPermission());
        var join = new JoinReceivingCycle(
            executor, store, synchronize, parser, permission, TimeProvider.System);
        var zero = new ConfirmZeroReceived(executor, store, permission, TimeProvider.System);
        var complete = new CompleteReceivingOrder(
            executor, store, synchronize, permission, TimeProvider.System);
        var reopen = new ReopenReceivingOrder(
            executor, store, permission, TimeProvider.System);

        Assert.Equal(orderId, Value(await join.ExecuteAsync(new JoinReceivingCycleRequest(
            Guid.NewGuid(), orderId, firstLocation.Id, "manager"))));
        Assert.Equal(skuId, Value(await zero.ExecuteAsync(new ConfirmZeroReceivedRequest(
            Guid.NewGuid(), orderId, skuId, "manager"))));
        var firstComplete = new CompleteReceivingOrderRequest(
            Guid.NewGuid(), orderId, "manager");
        Assert.Equal(orderId, Value(await complete.ExecuteAsync(firstComplete)));

        responses[sourceKey] = Source(sourceKey, warehouseId, $$"""
            [{"LineNumber":"1","Номенклатура_Key":"{{skuId}}","Количество":3}]
            """);
        Assert.True((await synchronize.ExecuteAsync(sourceKey)).IsSuccess);
        var reopenRequest = new ReopenReceivingOrderRequest(
            Guid.NewGuid(), orderId, "manager", true);
        Assert.Equal(orderId, Value(await reopen.ExecuteAsync(reopenRequest)));
        Assert.Equal(orderId, Value(await reopen.ExecuteAsync(reopenRequest)));
        Assert.Equal(orderId, Value(await join.ExecuteAsync(new JoinReceivingCycleRequest(
            Guid.NewGuid(), orderId, secondLocation.Id, "manager"))));
        Assert.Equal(orderId, Value(await complete.ExecuteAsync(new CompleteReceivingOrderRequest(
            Guid.NewGuid(), orderId, "manager"))));
        Assert.Equal(orderId, Value(await complete.ExecuteAsync(firstComplete)));

        await using var verify = database.CreateDbContext();
        var cycles = await verify.ReceivingCycles.Include(x => x.SourceVersions)
            .Where(x => x.ReceivingOrderId == orderId)
            .OrderBy(x => x.Number).ToArrayAsync();
        Assert.Equal(2, cycles.Length);
        Assert.Equal(firstLocation.Id, cycles[0].ReceivingLocationId);
        Assert.Equal(secondLocation.Id, cycles[1].ReceivingLocationId);
        Assert.Equal(2m, JsonDocument.Parse(cycles[0].PlanSnapshotJson!)
            .RootElement[0].GetProperty("Quantity").GetDecimal());
        Assert.Equal(3m, JsonDocument.Parse(cycles[1].PlanSnapshotJson!)
            .RootElement[0].GetProperty("Quantity").GetDecimal());
        Assert.Equal(0m, JsonDocument.Parse(cycles[0].FactSnapshotJson!)
            .RootElement[0].GetProperty("Quantity").GetDecimal());
        Assert.Single(cycles[0].SourceVersions);
        Assert.Single(cycles[1].SourceVersions);
        Assert.NotEqual(cycles[0].SourceVersions.Single().ReceivingSourceVersionId,
            cycles[1].SourceVersions.Single().ReceivingSourceVersionId);
        Assert.Equal(2, await verify.ReceivingOrderParticipants.CountAsync(x =>
            x.ReceivingOrderId == orderId));
        Assert.False(await verify.InventoryMovements.AnyAsync(x => x.RecorderId == orderId));
    }

    [Fact]
    public async Task Changing_sources_rebuilds_plan_and_last_detach_keeps_order_visible()
    {
        var warehouseId = Guid.NewGuid();
        var skuId = Guid.NewGuid();
        var firstKey = Guid.NewGuid();
        var secondKey = Guid.NewGuid();
        await using (var seed = database.CreateDbContext())
        {
            seed.Warehouses.Add(new Warehouse { Id = warehouseId, Name = "Changing sources" });
            seed.StockKeepingUnits.Add(new StockKeepingUnit { Id = skuId, Name = "Source SKU" });
            await seed.SaveChangesAsync();
        }

        var responses = new Dictionary<Guid, string>
        {
            [firstKey] = Source(firstKey, warehouseId, $$"""
                [{"LineNumber":"1","Номенклатура_Key":"{{skuId}}","Количество":2}]
                """),
            [secondKey] = Source(secondKey, warehouseId, $$"""
                [{"LineNumber":"1","Номенклатура_Key":"{{skuId}}","Количество":3}]
                """)
        };
        var parser = new ReceivingSourceParser();
        var synchronization = new SynchronizeReceivingSource(
            new StubReader(responses), parser,
            new ReceivingSourceStore(database, parser), TimeProvider.System);
        Assert.True((await synchronization.ExecuteAsync(firstKey)).IsSuccess);
        var orderId = Value(await new CreateReceivingOrder(
            new ReceivingOrderStore(database), parser, TimeProvider.System)
            .ExecuteAsync([firstKey]));
        var query = new ReceivingWorkQueryStore(database);
        var change = new ChangeReceivingOrderSources(
            new CommandExecutor(new CommandExecutionStore(database)),
            new ReceivingOrderSourceChangeStore(), query, parser, synchronization,
            new WarehousePermission(new ManagerPermission()), TimeProvider.System);

        async Task ChangeAsync(Guid key, ReceivingSourceChangeKind kind)
        {
            var result = await change.ExecuteAsync(new ChangeReceivingOrderSourcesRequest(
                Guid.NewGuid(), orderId, key, "manager", kind,
                ReceivingSourceChangeChannel.Web));
            Assert.Equal(orderId, Value(result));
        }

        await ChangeAsync(secondKey, ReceivingSourceChangeKind.Attach);
        await using (var attached = database.CreateDbContext())
            Assert.Equal(5m, (await attached.ReceivingOrderItems.SingleAsync(x =>
                x.ReceivingOrderId == orderId && x.StockKeepingUnitId == skuId))
                .CurrentPlanQuantity);

        await ChangeAsync(firstKey, ReceivingSourceChangeKind.Detach);
        await ChangeAsync(secondKey, ReceivingSourceChangeKind.Detach);
        await using var verify = database.CreateDbContext();
        var item = await verify.ReceivingOrderItems.SingleAsync(x =>
            x.ReceivingOrderId == orderId && x.StockKeepingUnitId == skuId);
        Assert.Equal(0m, item.CurrentPlanQuantity);
        Assert.Null(item.FactQuantity);
        Assert.Empty((await query.GetOrderAsync(orderId, "manager", default))!.SourceRefKeys);
        Assert.Equal(3, await verify.CommandReceipts.CountAsync(x =>
            x.CommandType == "receiving.order.change-sources"));
        Assert.Equal(3, await verify.WarehouseAuditEvents.CountAsync(x =>
            x.ReceivingOrderId == orderId && x.CommandType == "receiving.order.change-sources"));
    }

    [Fact]
    public async Task Several_sources_create_one_order_with_distinct_links_and_aggregated_sku_plan()
    {
        var warehouseId = Guid.NewGuid();
        var otherWarehouseId = Guid.NewGuid();
        var skuId = Guid.NewGuid();
        var uncertainSkuId = Guid.NewGuid();
        var firstKey = Guid.NewGuid();
        var secondKey = Guid.NewGuid();
        var unpostedKey = Guid.NewGuid();
        var otherKey = Guid.NewGuid();
        await using (var seed = database.CreateDbContext())
        {
            seed.Warehouses.AddRange(
                new Warehouse { Id = warehouseId, Name = "Receiving creation" },
                new Warehouse { Id = otherWarehouseId, Name = "Other warehouse" });
            seed.StockKeepingUnits.AddRange(
                new StockKeepingUnit { Id = skuId, Name = "Repeated SKU" },
                new StockKeepingUnit { Id = uncertainSkuId, Name = "Uncertain SKU" });
            await seed.SaveChangesAsync();
        }

        var responses = new Dictionary<Guid, string>
        {
            [firstKey] = Source(firstKey, warehouseId, $$"""
                [{"LineNumber":"1","Номенклатура_Key":"{{skuId}}","Количество":2},
                 {"LineNumber":"2","Номенклатура_Key":"{{skuId}}","Количество":3},
                 {"LineNumber":"3","Номенклатура_Key":"{{uncertainSkuId}}","Количество":"unsupported"}]
                """),
            [secondKey] = Source(secondKey, warehouseId, $$"""
                [{"LineNumber":"1","Номенклатура_Key":"{{skuId}}","Количество":4},
                 {"LineNumber":"2","Номенклатура_Key":"{{uncertainSkuId}}","Количество":7}]
                """),
            [unpostedKey] = Source(unpostedKey, warehouseId, $$"""
                [{"LineNumber":"1","Номенклатура_Key":"{{skuId}}","Количество":100}]
                """, posted: false),
            [otherKey] = Source(otherKey, otherWarehouseId, "[]")
        };
        var parser = new ReceivingSourceParser();
        var synchronization = new SynchronizeReceivingSource(
            new StubReader(responses), parser, new ReceivingSourceStore(database, parser), TimeProvider.System);
        foreach (var key in responses.Keys)
            Assert.True((await synchronization.ExecuteAsync(key)).IsSuccess);

        var store = new ReceivingOrderStore(database);
        var list = new GetReceivingSources(store, parser);
        var available = (await list.ListUnlinkedAsync())
            .Where(x => responses.ContainsKey(x.RefKey)).ToList();
        Assert.Equal(4, available.Count);
        Assert.Equal(0m, available.Single(x => x.RefKey == unpostedKey).PlanBySku[skuId]);
        var create = new CreateReceivingOrder(store, parser, TimeProvider.System);
        var wrongWarehouse = await create.ExecuteAsync([firstKey, otherKey]);
        Assert.False(wrongWarehouse.IsSuccess);

        var first = Value(await create.ExecuteAsync([firstKey, firstKey, secondKey, unpostedKey]));
        Assert.NotEqual(firstKey, first);
        Assert.NotEqual(secondKey, first);
        Assert.Equal(first, Value(await create.ExecuteAsync([secondKey, firstKey, unpostedKey])));

        await using var verify = database.CreateDbContext();
        Assert.Equal(1, await verify.ReceivingOrders.CountAsync(x => x.Id == first));
        var links = await verify.ReceivingOrderSourceLinks
            .Where(x => x.ReceivingOrderId == first && x.DetachedAtUtc == null)
            .ToListAsync();
        Assert.Equal(3, links.Count);
        var items = await verify.ReceivingOrderItems
            .Where(x => x.ReceivingOrderId == first).ToListAsync();
        Assert.Equal(2, items.Count);
        Assert.Equal(9m, items.Single(x => x.StockKeepingUnitId == skuId).CurrentPlanQuantity);
        Assert.Null(items.Single(x => x.StockKeepingUnitId == uncertainSkuId).CurrentPlanQuantity);
        Assert.All(items, x => Assert.Null(x.FactQuantity));
        Assert.Equal(1, await verify.ReceivingCycles.CountAsync(x => x.ReceivingOrderId == first));
        var unlinked = (await list.ListUnlinkedAsync())
            .Where(x => responses.ContainsKey(x.RefKey)).ToList();
        Assert.Single(unlinked);
        Assert.Equal(otherKey, unlinked[0].RefKey);

        var revision = (await verify.ReceivingOrders.SingleAsync(x => x.Id == first))
            .OperationalRevision;
        var firstSourceId = await verify.ReceivingSourceDocuments
            .Where(x => x.RefKey == firstKey).Select(x => x.Id).SingleAsync();
        responses[firstKey] = Source(firstKey, warehouseId, $$"""
            [{"LineNumber":"1","Номенклатура_Key":"{{skuId}}","Количество":6},
             {"LineNumber":"2","Номенклатура_Key":"{{uncertainSkuId}}","Количество":"unsupported"}]
            """);
        Assert.True((await synchronization.ExecuteAsync(firstKey)).IsSuccess);
        Assert.True((await synchronization.ExecuteAsync(firstKey)).IsSuccess);
        await using var updated = database.CreateDbContext();
        Assert.Equal(2, await updated.ReceivingSourceVersions.CountAsync(x =>
            x.ReceivingSourceDocumentId == firstSourceId));
        var current = await updated.ReceivingOrderItems
            .Where(x => x.ReceivingOrderId == first).ToListAsync();
        Assert.Equal(10m, current.Single(x => x.StockKeepingUnitId == skuId).CurrentPlanQuantity);
        Assert.Null(current.Single(x => x.StockKeepingUnitId == uncertainSkuId).CurrentPlanQuantity);
        Assert.All(current, x => Assert.Null(x.FactQuantity));
        Assert.Equal(revision + 1, (await updated.ReceivingOrders.SingleAsync(x =>
            x.Id == first)).OperationalRevision);
        Assert.False(await updated.InventoryMovements.AnyAsync(x => x.RecorderId == first));
    }

    private static string Source(Guid refKey, Guid warehouseId, string items, bool posted = true) => $$"""
        {"value":[{"Ref_Key":"{{refKey}}","Склад_Key":"{{warehouseId}}",
         "Posted":{{posted.ToString().ToLowerInvariant()}},"DeletionMark":false,"Number":"Test","Товары":{{items}}}]}
        """;

    private sealed class StubReader(IReadOnlyDictionary<Guid, string> responses) : IReceivingSourceReader
    {
        public Task<OperationResult<ReceivingSourceRead>> ReadAsync(Guid refKey, CancellationToken ct) =>
            Task.FromResult<OperationResult<ReceivingSourceRead>>(
                new ReceivingSourceRead(refKey, responses[refKey]));
    }

    private sealed class ManagerPermission : IWarehousePermissionStore
    {
        public Task<bool> HasManagerRoleAsync(string userId, CancellationToken ct) =>
            Task.FromResult(true);

        public Task<bool> HasReceivingRoleAsync(string userId, CancellationToken ct) =>
            Task.FromResult(true);
    }
}

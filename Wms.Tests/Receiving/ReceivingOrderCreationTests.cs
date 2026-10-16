using Microsoft.EntityFrameworkCore;
using Wms.Application.Receiving;
using Wms.Common;
using Wms.Data;
using Wms.Domain;
using Wms.Integration.OneS;
using Wms.Tests.Infrastructure;
using static Wms.Tests.Infrastructure.TestSupport;

namespace Wms.Tests.Receiving;

[Collection(SqlDatabaseCollection.Name)]
public sealed class ReceivingOrderCreationTests(SqlDatabase database)
{
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
        var available = await list.ListUnlinkedAsync();
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
        var unlinked = await list.ListUnlinkedAsync();
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
}

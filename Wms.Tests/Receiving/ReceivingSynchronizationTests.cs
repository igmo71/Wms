using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Wms.Application.Authorization;
using Wms.Application.CommandExecution;
using Wms.Application.Inventory;
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
public sealed class ReceivingSynchronizationTests(SqlDatabase database)
{
    [Fact]
    public async Task Source_changes_and_failed_refresh_preserve_posted_fact_and_completed_cycle()
    {
        var warehouseId = Guid.NewGuid();
        var skuId = Guid.NewGuid();
        var firstKey = Guid.NewGuid();
        var secondKey = Guid.NewGuid();
        var zone = Zone(warehouseId, "RC", ZoneType.Receiving);
        var location = Location(warehouseId, zone, 1);
        var batch = Value(LicensePlateNumberBatch.Issue(1, "manager", DateTimeOffset.UtcNow));
        var lpnId = batch.Labels.Single().Id;
        await using (var seed = database.CreateDbContext())
        {
            seed.AddRange(new Warehouse { Id = warehouseId, Name = "Source changes" },
                new StockKeepingUnit { Id = skuId, Name = "Source SKU" },
                zone, location, batch);
            await seed.SaveChangesAsync();
        }

        string Source(Guid key, int quantity, bool posted = true,
            bool deleted = false, bool hasLine = true)
        {
            var lines = hasLine
                ? $$"""[{"LineNumber":"1","Номенклатура_Key":"{{skuId}}","Количество":{{quantity}}}]"""
                : "[]";
            return $$"""
                {"value":[{"Ref_Key":"{{key}}","Склад_Key":"{{warehouseId}}",
                "Отправитель":"11111111-1111-1111-1111-111111111111",
                "Posted":{{posted.ToString().ToLowerInvariant()}},
                "DeletionMark":{{deleted.ToString().ToLowerInvariant()}},
                "Number":"Source", "Товары":{{lines}}}]}
                """;
        }
        var responses = new Dictionary<Guid, string>
        {
            [firstKey] = Source(firstKey, 2),
            [secondKey] = Source(secondKey, 3)
        };
        var reader = new MutableSourceReader(responses);
        var parser = new ReceivingSourceParser();
        var synchronize = new SynchronizeReceivingSource(reader, parser,
            new ReceivingSourceStore(database, parser), TimeProvider.System);
        Assert.True((await synchronize.ExecuteAsync(firstKey)).IsSuccess);
        Assert.True((await synchronize.ExecuteAsync(secondKey)).IsSuccess);
        var orderId = Value(await new CreateReceivingOrder(
            new ReceivingOrderStore(database), parser, TimeProvider.System)
            .ExecuteAsync(new CreateReceivingOrderRequest(
                Guid.NewGuid(), [firstKey, secondKey], "manager")));

        await using (var prepare = database.CreateDbContext())
        {
            var order = await prepare.ReceivingOrders
                .Include(x => x.Cycles).Include(x => x.Participants)
                .Include(x => x.Items).Include(x => x.LicensePlateNumbers)
                .ThenInclude(x => x.Items).SingleAsync(x => x.Id == orderId);
            var lpn = await prepare.LicensePlateNumbers.Include(x => x.Items)
                .SingleAsync(x => x.Id == lpnId);
            var now = DateTimeOffset.UtcNow;
            Value(order.JoinCurrentCycle(location.Id, "manager", now));
            prepare.ReceivingOrderParticipants.Add(Assert.Single(order.Participants));
            Assert.True(Value(order.OpenReceivingLpn(lpn, "manager", now)));
            Success(order.EditReceivingLpn(lpn, skuId, null, 1m, "manager", now, false));
            prepare.LicensePlateNumberItems.Add(Assert.Single(lpn.Items));
            await prepare.SaveChangesAsync();
        }

        var executor = new CommandExecutor(new CommandExecutionStore(database));
        var workStore = new ReceivingWorkStore(database);
        var permission = new WarehousePermission(new ManagerPermission());
        var close = new CloseReceivingLpn(executor, workStore, synchronize, parser,
            new PostInventoryMovement.Handler(new InventoryPostingStore()),
            permission, TimeProvider.System);
        var closeRequest = new CloseReceivingLpnCommand(Guid.NewGuid(), orderId, lpnId, "manager");
        Assert.Equal(5m, Value(await close.ExecuteAsync(closeRequest)).UsedPlan);
        var complete = new CompleteReceivingOrder(executor, workStore, synchronize,
            permission, TimeProvider.System);
        Assert.Equal(orderId, Value(await complete.ExecuteAsync(
            new CompleteReceivingOrderRequest(Guid.NewGuid(), orderId, "manager"))));

        async Task AssertCurrentPlanAsync(decimal expected)
        {
            await using var check = database.CreateDbContext();
            var item = await check.ReceivingOrderItems.SingleAsync(x =>
                x.ReceivingOrderId == orderId && x.StockKeepingUnitId == skuId);
            Assert.Equal(expected, item.CurrentPlanQuantity);
            Assert.Equal(1m, item.FactQuantity);
        }

        responses[firstKey] = Source(firstKey, 2, posted: false);
        Assert.True((await synchronize.ExecuteAsync(firstKey)).IsSuccess);
        await AssertCurrentPlanAsync(3m);
        responses[firstKey] = Source(firstKey, 2);
        Assert.True((await synchronize.ExecuteAsync(firstKey)).IsSuccess);
        await AssertCurrentPlanAsync(5m);
        responses[firstKey] = Source(firstKey, 2, deleted: true);
        Assert.True((await synchronize.ExecuteAsync(firstKey)).IsSuccess);
        await AssertCurrentPlanAsync(3m);
        responses[firstKey] = Source(firstKey, 2);
        Assert.True((await synchronize.ExecuteAsync(firstKey)).IsSuccess);
        responses[firstKey] = Source(firstKey, 2, hasLine: false);
        var lastGoodVersion = Value(await synchronize.ExecuteAsync(firstKey)).Id;
        await AssertCurrentPlanAsync(3m);

        reader.FailKey = firstKey;
        Assert.False((await synchronize.ExecuteAsync(firstKey)).IsSuccess);
        await AssertCurrentPlanAsync(3m);
        await using (var failedCheck = database.CreateDbContext())
        {
            var failedSource = await failedCheck.ReceivingSourceDocuments
                .SingleAsync(x => x.RefKey == firstKey);
            Assert.Equal("1С недоступна.", failedSource.LastAttemptError);
            Assert.Equal(lastGoodVersion, failedSource.LastSuccessfulVersionId);
        }
        reader.FailKey = null;
        var currentVersion = Value(await synchronize.ExecuteAsync(firstKey)).Id;
        Assert.Equal(currentVersion, Value(await synchronize.ExecuteAsync(firstKey)).Id);
        Assert.Equal(5m, Value(await close.ExecuteAsync(closeRequest)).UsedPlan);

        await using var verify = database.CreateDbContext();
        var source = await verify.ReceivingSourceDocuments.SingleAsync(x => x.RefKey == firstKey);
        Assert.Equal(currentVersion, source.LastSuccessfulVersionId);
        Assert.Null(source.LastAttemptError);
        var item = await verify.ReceivingOrderItems.SingleAsync(x =>
            x.ReceivingOrderId == orderId && x.StockKeepingUnitId == skuId);
        Assert.Equal(3m, item.CurrentPlanQuantity);
        Assert.Equal(1m, item.FactQuantity);
        var cycle = await verify.ReceivingCycles.Include(x => x.SourceVersions)
            .SingleAsync(x => x.ReceivingOrderId == orderId);
        Assert.False(cycle.SourceCheckFailed);
        Assert.Equal(5m, JsonDocument.Parse(cycle.PlanSnapshotJson!).RootElement[0]
            .GetProperty("Quantity").GetDecimal());
        Assert.Equal(1m, JsonDocument.Parse(cycle.FactSnapshotJson!).RootElement[0]
            .GetProperty("Quantity").GetDecimal());
        Assert.Equal(2, cycle.SourceVersions.Count);
        Assert.DoesNotContain(cycle.SourceVersions, x => x.ReceivingSourceVersionId == currentVersion);
        Assert.Equal(ReceivingOrderStatus.Received,
            (await verify.ReceivingOrders.SingleAsync(x => x.Id == orderId)).Status);
        Assert.Equal(1, await verify.InventoryMovements.CountAsync(x => x.LicensePlateNumberId == lpnId));
        Assert.Equal(1, await verify.CommandReceipts.CountAsync(x =>
            x.CommandType == "receiving.lpn.close"));
    }

    [Fact]
    public async Task Sql_source_projection_keeps_known_sku_plans_when_other_lines_are_unresolved()
    {
        var refKey = Guid.NewGuid();
        var source = Value(ReceivingSourceDocument.Create(refKey));
        var version = Value(ReceivingSourceVersion.Create(
            source.Id, DateTimeOffset.UtcNow, new string('A', 64), "{}"));
        var repeatedSku = new StockKeepingUnit { Id = Guid.NewGuid(), Name = "Repeated SKU" };
        var unresolvedSku = new StockKeepingUnit { Id = Guid.NewGuid(), Name = "Unresolved quantity" };
        var independentSku = new StockKeepingUnit { Id = Guid.NewGuid(), Name = "Independent SKU" };
        var lines = new[]
        {
            Value(ReceivingSourceLineProjection.Create(version.Id, 1, Guid.NewGuid(), repeatedSku.Id, "2", 2m, null)),
            Value(ReceivingSourceLineProjection.Create(version.Id, 2, Guid.NewGuid(), repeatedSku.Id, "3", 3m, null)),
            Value(ReceivingSourceLineProjection.Create(version.Id, 3, Guid.NewGuid(), unresolvedSku.Id,
                "unsupported", null, "Количество 1С не поддерживается")),
            Value(ReceivingSourceLineProjection.Create(version.Id, 4, Guid.NewGuid(), unresolvedSku.Id, "4", 4m, null)),
            Value(ReceivingSourceLineProjection.Create(version.Id, 5, Guid.NewGuid(), null,
                "9", 9m, "SKU 1С не найден")),
            Value(ReceivingSourceLineProjection.Create(version.Id, 6, Guid.NewGuid(), independentSku.Id, "7", 7m, null))
        };

        await using (var seed = database.CreateDbContext())
        {
            seed.AddRange(source, version, repeatedSku, unresolvedSku, independentSku);
            seed.AddRange(lines);
            await seed.SaveChangesAsync();
        }

        await using (var duplicate = database.CreateDbContext())
        {
            duplicate.Add(Value(ReceivingSourceDocument.Create(refKey)));
            await Assert.ThrowsAsync<DbUpdateException>(() => duplicate.SaveChangesAsync());
        }

        await using var verify = database.CreateDbContext();
        Assert.Equal(1, await verify.ReceivingSourceDocuments.CountAsync(x => x.RefKey == refKey));
        var storedVersion = await verify.ReceivingSourceVersions
            .Include(x => x.Lines)
            .SingleAsync(x => x.Id == version.Id);
        Assert.Equal(6, storedVersion.Lines.Count);
        Assert.Equal(2, storedVersion.Lines.Count(x => x.StockKeepingUnitId == repeatedSku.Id));

        var plan = storedVersion.CalculatePlan();
        Assert.Equal(5m, plan.QuantityFor(repeatedSku.Id));
        Assert.Null(plan.QuantityFor(unresolvedSku.Id));
        Assert.Equal(7m, plan.QuantityFor(independentSku.Id));
        Assert.Equal(0m, plan.QuantityFor(Guid.NewGuid()));
        Assert.True(plan.HasUnknownSku);
    }

    private sealed class MutableSourceReader(IReadOnlyDictionary<Guid, string> responses)
        : IReceivingSourceReader
    {
        public Guid? FailKey { get; set; }

        public Task<OperationResult<ReceivingSourceRead>> ReadAsync(Guid refKey,
            CancellationToken ct) => Task.FromResult<OperationResult<ReceivingSourceRead>>(
            FailKey == refKey ? OperationError.Failure("1С недоступна.")
                : new ReceivingSourceRead(refKey, responses[refKey]));
    }

    private sealed class ManagerPermission : IWarehousePermissionStore
    {
        public Task<bool> HasManagerRoleAsync(string userId, CancellationToken ct) =>
            Task.FromResult(true);

        public Task<bool> HasReceivingRoleAsync(string userId, CancellationToken ct) =>
            Task.FromResult(true);
    }
}

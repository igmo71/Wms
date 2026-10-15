using Microsoft.EntityFrameworkCore;
using Wms.Domain;
using Wms.Domain.Enums;
using Wms.Tests.Infrastructure;
using static Wms.Tests.Infrastructure.TestSupport;

namespace Wms.Tests.Receiving;

[Collection(SqlDatabaseCollection.Name)]
public sealed class ReceivingSynchronizationTests(SqlDatabase database)
{
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

    [Fact]
    public void Comments_do_not_change_the_checkpoint_but_quantity_differences_block()
    {
        var now = DateTimeOffset.UtcNow;
        var source = new ReceivingOrderImportSnapshot(
            Guid.NewGuid(), false, true, "Synchronization", now.UtcDateTime, Guid.NewGuid(), null,
            ReceivingOrderStatus.ReadyForReceiving, default, WarehouseOperation.VendorReceipt,
            BusinessOperation.VendorPurchase, Guid.NewGuid(), default, Guid.NewGuid(), null,
            [new(1, Guid.NewGuid(), 18m, 18m, null)]);
        var order = Value(ReceivingOrder.Create(source, now));
        Success(order.SetReceivingLocation(Guid.NewGuid()));
        Success(order.SetInReceiving(now, "synchronization"));
        Success(order.JoinReceiving(now, "synchronization"));
        var lpn = Value(LicensePlateNumberBatch.Issue(1, "synchronization", now)).Labels.Single();
        Value(order.OpenOrCreateLicensePlateNumber(lpn, now, "synchronization"));
        Success(order.SetLicensePlateNumberItemQuantity(
            lpn, source.Items.Single().StockKeepingUnitId, 15m, now, "synchronization", false));
        var sourceItem = source.Items.Single();

        var target = source with
        {
            Status = ReceivingOrderStatus.Received,
            Items = [new(1, sourceItem.StockKeepingUnitId, 15m, 15m, "Changed externally")]
        };
        var targetItem = target.Items.Single();
        var baseline = ReceivingOrderSynchronizationComparer.CompareReceivedTarget(order, target);
        var changedComment = ReceivingOrderSynchronizationComparer.CompareReceivedTarget(
            order, target with { Items = [targetItem with { Comment = "Another comment" }] });
        var changedQuantity = ReceivingOrderSynchronizationComparer.CompareReceivedTarget(
            order, target with { Items = [targetItem with { Quantity = 18m }] });

        Assert.Equal(OrderSynchronizationLevel.Synchronized, baseline.Level);
        Assert.Equal(baseline.Fingerprint, changedComment.Fingerprint);
        Assert.Equal(OrderSynchronizationLevel.Blocking, changedQuantity.Level);
        Assert.Contains(changedQuantity.Differences, difference => difference.FieldCode == "items[1].quantity");
    }
}

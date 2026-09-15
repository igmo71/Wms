using Wms.Domain;
using Wms.Domain.Enums;
using static Wms.Tests.Infrastructure.TestSupport;

namespace Wms.Tests.Receiving;

public sealed class ReceivingSynchronizationTests
{
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
        Success(order.UpdateItemFact(1, 15m, "Shortage note"));
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

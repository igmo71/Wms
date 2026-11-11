using Wms.Application.Inventory;
using Wms.Application.Replenishment;
using Wms.Domain;
using Wms.Domain.Enums;
using static Wms.Tests.Infrastructure.TestSupport;

namespace Wms.Tests.Replenishment;

public sealed class ReplenishmentProposalSafetyTests
{
    [Fact]
    public void Existing_order_and_later_storage_do_not_create_excess_replenishment()
    {
        var warehouseId = Guid.NewGuid();
        var assemblyId = Guid.NewGuid();
        var skuId = Guid.NewGuid();
        var firstSource = Guid.NewGuid();
        var secondSource = Guid.NewGuid();
        var destinationId = Guid.NewGuid();
        var earlier = new DateOnly(2026, 5, 3);
        var later = new DateOnly(2026, 5, 8);
        var ordered = Value(ReplenishmentTask.Order(Guid.NewGuid(), assemblyId,
            warehouseId, skuId, 2, firstSource, destinationId, "worker",
            DateTimeOffset.UtcNow));
        var empty = new HashSet<Guid>();
        var result = Value(new BuildReplenishmentProposals().Execute(
            new ReplenishmentProposalRequest(assemblyId, warehouseId,
                new DateOnly(2026, 5, 1),
                new Dictionary<Guid, decimal> { [skuId] = 7 }, [],
                [Stock(firstSource, "A", earlier, 4),
                 Stock(secondSource, "B", earlier, 2),
                 Stock(destinationId, "C", later, 3)],
                [new ReplenishmentLocation(firstSource, "A", ZoneType.Reserve,
                    true, false, false, new HashSet<Guid> { skuId }, empty),
                 new ReplenishmentLocation(secondSource, "B", ZoneType.Reserve,
                    true, false, false, new HashSet<Guid> { skuId }, empty),
                 new ReplenishmentLocation(destinationId, "C", ZoneType.Storage,
                    true, false, false, new HashSet<Guid> { skuId }, empty)],
                [ordered])));

        Assert.Empty(result.Deficits);
        Assert.Equal(4m, result.Proposals.Sum(x => x.Quantity));
        Assert.Equal(2m, result.Proposals.Single(x =>
            x.SourceLocationId == firstSource).Quantity);
        Assert.Equal(2m, result.Proposals.Single(x =>
            x.SourceLocationId == secondSource).Quantity);
        Assert.All(result.Proposals, proposal =>
        {
            Assert.Equal(destinationId, proposal.DestinationLocationId);
            Assert.All(proposal.Groups, group => Assert.Equal(earlier,
                group.ExpirationDate));
        });

        StockAvailability Stock(Guid locationId, string code, DateOnly date,
            decimal quantity) => new(
            new StockGroupKey(warehouseId, locationId, skuId, date), code,
            quantity, [], 0, null);
    }
}

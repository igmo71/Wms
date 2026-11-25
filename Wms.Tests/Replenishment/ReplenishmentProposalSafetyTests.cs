using Wms.Application.Inventory;
using Wms.Application.Picking;
using Wms.Application.Replenishment;
using Wms.Domain;
using Wms.Domain.Enums;
using static Wms.Tests.Infrastructure.TestSupport;

namespace Wms.Tests.Replenishment;

public sealed class ReplenishmentProposalSafetyTests
{
    [Fact]
    public void Existing_order_and_later_storage_count_route_quantity_once()
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
        var result = Value(new BuildReplenishmentProposals().Execute(
            new ReplenishmentProposalRequest(assemblyId, warehouseId,
                new DateOnly(2026, 5, 1),
                new Dictionary<Guid, decimal> { [skuId] = 8 },
                [new PickingRouteStep(destinationId, "C", skuId, later, 1, false)],
                [Stock(firstSource, "A", earlier, 4),
                 Stock(secondSource, "B", earlier, 2),
                 Stock(destinationId, "C", later, 2) with
                 { RouteReservations = [new StockRouteReservationView(assemblyId, 1)] }],
                [new ReplenishmentLocation(firstSource, "A", ZoneType.Reserve,
                    true, false, false, 1, new HashSet<Guid> { skuId }),
                 new ReplenishmentLocation(secondSource, "B", ZoneType.Reserve,
                    true, false, false, 1, new HashSet<Guid> { skuId }),
                 new ReplenishmentLocation(destinationId, "C", ZoneType.Storage,
                    true, false, false, 1, new HashSet<Guid> { skuId })],
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

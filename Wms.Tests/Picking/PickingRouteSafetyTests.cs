using Wms.Application.Inventory;
using Wms.Application.Picking;
using Wms.Domain.Enums;
using static Wms.Tests.Infrastructure.TestSupport;

namespace Wms.Tests.Picking;

public sealed class PickingRouteSafetyTests
{
    [Fact]
    public void Reserved_route_ignores_free_and_foreign_stock_and_keeps_the_rest_of_a_held_pallet()
    {
        var warehouse = Guid.NewGuid();
        var assembly = Guid.NewGuid();
        var sku = Guid.NewGuid();
        var pallet = Location("1A01A1", ZoneType.Reserve);
        var unallocated = Location("1A02A1", ZoneType.Storage);
        var storage = Location("1A03A1", ZoneType.Storage);
        var date = new DateOnly(2026, 5, 3);
        var hold = new StockHoldView(StorageLocationLockOwnerType.PickingAssembly, assembly, "pallet", "operator");
        var route = Value(new BuildPickingRoute().ExecuteReserved(new PickingRouteRequest(
            assembly, warehouse, new DateOnly(2026, 5, 1),
            new Dictionary<Guid, decimal> { [sku] = 4 },
            [Stock(warehouse, pallet, sku, date, 4) with
             { Reservations = [new StockReservationView(Guid.NewGuid(), assembly, 4, "operator")], Hold = hold },
             Stock(warehouse, pallet, sku, date.AddDays(1), 2) with
             { RouteReservations = [new StockRouteReservationView(assembly, 2)], Hold = hold },
             Stock(warehouse, unallocated, sku, date, 4) with
             { RouteReservations = [new StockRouteReservationView(Guid.NewGuid(), 2)] },
             Stock(warehouse, storage, sku, date.AddDays(2), 2) with
             { RouteReservations = [new StockRouteReservationView(assembly, 1)] }],
            [pallet, unallocated, storage])));

        Assert.Equal(new[] { pallet.Id, storage.Id }, route.Steps.Select(x => x.StorageLocationId));
        Assert.Equal(new[] { 2m, 1m }, route.Steps.Select(x => x.Quantity));
        Assert.True(route.Steps[0].DirectPalletPick);
        Assert.Equal(1m, Assert.Single(route.Gaps).Quantity);
    }

    [Fact]
    public void Earlier_date_blocks_a_convenient_later_pallet_and_allows_other_skus()
    {
        var warehouse = Guid.NewGuid();
        var assembly = Guid.NewGuid();
        var firstSku = Guid.NewGuid();
        var otherSku = Guid.NewGuid();
        var earlyStorage = Location("1A01A1", ZoneType.Storage);
        var laterPallet = Location("1A02A1", ZoneType.Reserve);
        var otherStorage = Location("1A03A1", ZoneType.Storage);
        var route = Value(new BuildPickingRoute().Execute(new PickingRouteRequest(
            assembly, warehouse, new DateOnly(2026, 5, 1),
            new Dictionary<Guid, decimal> { [firstSku] = 4, [otherSku] = 1 },
            [Stock(warehouse, earlyStorage, firstSku, new DateOnly(2026, 5, 3), 2),
             Stock(warehouse, laterPallet, firstSku, new DateOnly(2026, 5, 8), 2),
             Stock(warehouse, otherStorage, otherSku, new DateOnly(2026, 5, 4), 1)],
            [earlyStorage, laterPallet, otherStorage])));

        Assert.Equal(new[] { otherStorage.Id, earlyStorage.Id },
            route.Steps.Select(x => x.StorageLocationId));
        Assert.DoesNotContain(route.Steps, x => x.StorageLocationId == laterPallet.Id);
        Assert.Contains(route.Gaps, x => x.StockKeepingUnitId == firstSku
            && x.ExpirationDate == new DateOnly(2026, 5, 8) && x.Quantity == 2);
    }

    [Fact]
    public void Full_single_sku_pallet_precedes_storage_top_up_without_double_allocation()
    {
        var warehouse = Guid.NewGuid();
        var assembly = Guid.NewGuid();
        var sku = Guid.NewGuid();
        var pallet = Location("1A01A1", ZoneType.Reserve);
        var storage = Location("1A02A1", ZoneType.Storage);
        var date = new DateOnly(2026, 5, 3);
        var route = Value(new BuildPickingRoute().Execute(new PickingRouteRequest(
            assembly, warehouse, new DateOnly(2026, 5, 1),
            new Dictionary<Guid, decimal> { [sku] = 4 },
            [Stock(warehouse, pallet, sku, date, 2),
             Stock(warehouse, storage, sku, date, 3)],
            [pallet, storage])));

        Assert.Equal(new[] { pallet.Id, storage.Id },
            route.Steps.Select(x => x.StorageLocationId));
        Assert.Equal(new[] { 2m, 2m }, route.Steps.Select(x => x.Quantity));
        Assert.True(route.Steps[0].DirectPalletPick);
        Assert.Empty(route.Gaps);
    }

    private static PickingRouteLocation Location(string code, ZoneType zone) =>
        new(Guid.NewGuid(), code, zone, true, 1);

    private static StockAvailability Stock(Guid warehouse,
        PickingRouteLocation location, Guid sku, DateOnly date, decimal quantity) =>
        new(new StockGroupKey(warehouse, location.Id, sku, date), location.Code,
            quantity, [], 0, null);
}

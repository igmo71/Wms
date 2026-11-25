using Wms.Data;
using Wms.Domain;

namespace Wms.Application.Inventory.Movements;

internal static class UndatedInventoryAvailability
{
    public static IQueryable<AvailableBalance> Read(
        ApplicationDbContext db, IQueryable<InventoryBalance> balances) =>
        balances.Where(x => x.ExpirationDate == null)
            .Select(balance => new AvailableBalance
            {
                Balance = balance,
                CommittedQuantity = (db.PickingReservations
                        .Where(x => x.WarehouseId == balance.WarehouseId
                            && x.StorageLocationId == balance.StorageLocationId
                            && x.StockKeepingUnitId == balance.StockKeepingUnitId
                            && x.ExpirationDate == null && x.CompletedAtUtc == null)
                        .Sum(x => (decimal?)x.Quantity) ?? 0)
                    + (db.PickingRouteReservations
                        .Where(x => x.WarehouseId == balance.WarehouseId
                            && x.StorageLocationId == balance.StorageLocationId
                            && x.StockKeepingUnitId == balance.StockKeepingUnitId
                            && x.ExpirationDate == null)
                        .Sum(x => (decimal?)x.Quantity) ?? 0)
                    + (db.StockShortages
                        .Where(x => x.WarehouseId == balance.WarehouseId
                            && x.StorageLocationId == balance.StorageLocationId
                            && x.StockKeepingUnitId == balance.StockKeepingUnitId
                            && x.ExpirationDate == null && x.ResolvedAtUtc == null)
                        .Sum(x => (decimal?)(x.ExpectedQuantity - x.FoundQuantity)) ?? 0)
            });

    internal sealed class AvailableBalance
    {
        public required InventoryBalance Balance { get; init; }
        public decimal CommittedQuantity { get; init; }
    }
}

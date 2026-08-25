using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using System.Reflection;
using Wms.Domain;

namespace Wms.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<DeliveryDirection> DeliveryDirections => Set<DeliveryDirection>();
    public DbSet<Individual> Individuals => Set<Individual>();
    public DbSet<InventoryBalance> InventoryBalances => Set<InventoryBalance>();
    public DbSet<InventoryCount> InventoryCounts => Set<InventoryCount>();
    public DbSet<InventoryCountItem> InventoryCountItems => Set<InventoryCountItem>();
    public DbSet<InventoryMovement> InventoryMovements => Set<InventoryMovement>();
    public DbSet<InventoryTurnover> InventoryTurnovers => Set<InventoryTurnover>();
    public DbSet<MobileCommandReceipt> MobileCommandReceipts => Set<MobileCommandReceipt>();
    public DbSet<OrganizationalUnit> OrganizationalUnits => Set<OrganizationalUnit>();
    public DbSet<Partner> Partners => Set<Partner>();
    public DbSet<ReceivingOrder> ReceivingOrders => Set<ReceivingOrder>();
    public DbSet<ReceivingOrderItem> ReceivingOrderItems => Set<ReceivingOrderItem>();
    public DbSet<ShippingOrder> ShippingOrders => Set<ShippingOrder>();
    public DbSet<ShippingOrderBaseItem> ShippingOrderBaseItems => Set<ShippingOrderBaseItem>();
    public DbSet<ShippingOrderItem> ShippingOrderItems => Set<ShippingOrderItem>();
    public DbSet<SkuBarcode> SkuBarcodes => Set<SkuBarcode>();
    public DbSet<StockKeepingUnit> StockKeepingUnits => Set<StockKeepingUnit>();
    public DbSet<StorageLocation> StorageLocations => Set<StorageLocation>();
    public DbSet<InventoryTransfer> InventoryTransfers => Set<InventoryTransfer>();
    public DbSet<UnitOfMeasure> UnitsOfMeasure => Set<UnitOfMeasure>();
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();
    public DbSet<Zone> Zones => Set<Zone>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
    }
}

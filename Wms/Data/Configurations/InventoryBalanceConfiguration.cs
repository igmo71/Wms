using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain;

namespace Wms.Data.Configurations;

internal class InventoryBalanceConfiguration : IEntityTypeConfiguration<InventoryBalance>
{
    public void Configure(EntityTypeBuilder<InventoryBalance> builder)
    {
        builder.HasKey(x => x.Id);

        builder.HasOne(X => X.StockKeepingUnit).WithMany()
            .HasForeignKey(X => X.StockKeepingUnitId).HasPrincipalKey(X => X.Id)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(X => X.Warehouse).WithMany()
            .HasForeignKey(X => X.WarehouseId).HasPrincipalKey(X => X.Id)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(X => X.StorageLocation).WithMany()
            .HasForeignKey(X => X.StorageLocationId).HasPrincipalKey(X => X.Id)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new
        {
            x.StockKeepingUnitId,
            x.StorageLocationId,
            x.WarehouseId
        })
            .IsUnique();
    }
}

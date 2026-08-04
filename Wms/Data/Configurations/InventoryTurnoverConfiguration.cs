using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain;

namespace Wms.Data.Configurations;

internal class InventoryTurnoverConfiguration : IEntityTypeConfiguration<InventoryTurnover>
{
    public void Configure(EntityTypeBuilder<InventoryTurnover> builder)
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
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain;

namespace Wms.Data.Configurations;

internal class InventoryMovementConfiguration : IEntityTypeConfiguration<InventoryMovement>
{
    public void Configure(EntityTypeBuilder<InventoryMovement> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ConfirmedBy).HasMaxLength(DefaultConfiguration.Guid);

        builder.HasOne(x => x.Warehouse)
            .WithMany()
            .HasForeignKey(x => x.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        builder.HasOne(x => x.SourceStorageLocation)
            .WithMany()
            .HasForeignKey(x => x.SourceStorageLocationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.DestinationStorageLocation)
            .WithMany()
            .HasForeignKey(x => x.DestinationStorageLocationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.StockKeepingUnit)
            .WithMany()
            .HasForeignKey(x => x.StockKeepingUnitId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Common;
using Wms.Domain;

namespace Wms.Data.Configurations;

internal sealed class LicensePlateNumberPutawayConfiguration : IEntityTypeConfiguration<LicensePlateNumberPutaway>
{
    public void Configure(EntityTypeBuilder<LicensePlateNumberPutaway> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Quantity)
            .HasPrecision(WarehouseQuantity.Precision, WarehouseQuantity.Scale);
        builder.Property(x => x.ExpirationDate).HasColumnType("date");
        builder.Property(x => x.CreatedBy).HasMaxLength(450).IsRequired();
        builder.HasIndex(x => x.LicensePlateNumberId);
        builder.HasIndex(x => x.LicensePlateNumberItemId);
        builder.HasIndex(x => x.StockKeepingUnitId);
        builder.HasIndex(x => x.DestinationStorageLocationId);
        builder.HasIndex(x => x.InventoryMovementId).IsUnique();
        builder.HasOne(x => x.LicensePlateNumber).WithMany(x => x.Putaways)
            .HasForeignKey(x => x.LicensePlateNumberId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.LicensePlateNumberItem).WithMany()
            .HasForeignKey(x => x.LicensePlateNumberItemId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.StockKeepingUnit).WithMany()
            .HasForeignKey(x => x.StockKeepingUnitId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.SourceStorageLocation).WithMany()
            .HasForeignKey(x => x.SourceStorageLocationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.DestinationStorageLocation).WithMany()
            .HasForeignKey(x => x.DestinationStorageLocationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.InventoryMovement).WithOne()
            .HasForeignKey<LicensePlateNumberPutaway>(x => x.InventoryMovementId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

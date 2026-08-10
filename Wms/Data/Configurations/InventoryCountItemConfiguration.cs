using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain;

namespace Wms.Data.Configurations;

internal class InventoryCountItemConfiguration : IEntityTypeConfiguration<InventoryCountItem>
{
    public void Configure(EntityTypeBuilder<InventoryCountItem> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.LineNumber).IsRequired();

        builder.HasOne(x => x.InventoryCount)
            .WithMany(x => x.Items)
            .HasForeignKey(x => x.InventoryCountId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();

        builder.HasOne(x => x.StorageLocation)
            .WithMany()
            .HasForeignKey(x => x.StorageLocationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.StockKeepingUnit)
            .WithMany()
            .HasForeignKey(x => x.StockKeepingUnitId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.InventoryCountId, x.LineNumber });
    }
}

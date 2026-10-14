using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Common;
using Wms.Domain;

namespace Wms.Data.Configurations;

internal class InventoryCountItemConfiguration : IEntityTypeConfiguration<InventoryCountItem>
{
    public void Configure(EntityTypeBuilder<InventoryCountItem> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.LineNumber).IsRequired();
        builder.Property(x => x.ExpectedQuantity)
            .HasPrecision(WarehouseQuantity.Precision, WarehouseQuantity.Scale);
        builder.Property(x => x.CountedQuantity)
            .HasPrecision(WarehouseQuantity.Precision, WarehouseQuantity.Scale);
        builder.Property(x => x.CreatedBy).HasMaxLength(DefaultConfiguration.Guid).IsRequired();
        builder.Property(x => x.UpdatedBy).HasMaxLength(DefaultConfiguration.Guid);

        builder.HasOne(x => x.InventoryCount)
            .WithMany(x => x.Items)
            .HasForeignKey(x => x.InventoryCountId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();

        builder.HasOne(x => x.StockKeepingUnit)
            .WithMany()
            .HasForeignKey(x => x.StockKeepingUnitId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        builder.HasIndex(x => new { x.InventoryCountId, x.LineNumber });
        builder.HasIndex(x => new { x.InventoryCountId, x.StockKeepingUnitId })
            .IsUnique()
            .HasDatabaseName(DatabaseObjectNames.InventoryCountItemsSkuIndex);
    }
}

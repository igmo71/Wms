using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Common;
using Wms.Domain;

namespace Wms.Data.Configurations;

internal sealed class LicensePlateNumberItemConfiguration : IEntityTypeConfiguration<LicensePlateNumberItem>
{
    public void Configure(EntityTypeBuilder<LicensePlateNumberItem> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Quantity)
            .HasPrecision(WarehouseQuantity.Precision, WarehouseQuantity.Scale);
        builder.Property(x => x.CreatedBy).HasMaxLength(450).IsRequired();
        builder.Property(x => x.UpdatedBy).HasMaxLength(450);
        builder.Property(x => x.DeletedBy).HasMaxLength(450);
        builder.Ignore(x => x.IsDeleted);
        builder.HasIndex(x => new { x.LicensePlateNumberId, x.StockKeepingUnitId })
            .HasDatabaseName(DatabaseObjectNames.LicensePlateNumberItemsActiveSkuIndex)
            .HasFilter("[DeletedAtUtc] IS NULL")
            .IsUnique();
        builder.HasOne(x => x.LicensePlateNumber).WithMany(x => x.Items)
            .HasForeignKey(x => x.LicensePlateNumberId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.StockKeepingUnit).WithMany()
            .HasForeignKey(x => x.StockKeepingUnitId).OnDelete(DeleteBehavior.Restrict);
    }
}

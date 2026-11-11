using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Common;
using Wms.Domain;

namespace Wms.Data.Configurations;

internal sealed class PickingEntryConfiguration : IEntityTypeConfiguration<PickingEntry>
{
    public void Configure(EntityTypeBuilder<PickingEntry> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Quantity)
            .HasPrecision(WarehouseQuantity.Precision, WarehouseQuantity.Scale);
        builder.Property(x => x.ExpirationDate).HasColumnType("date");
        builder.Property(x => x.ConfirmedBy).HasMaxLength(DefaultConfiguration.Guid).IsRequired();
        builder.Property(x => x.LastChangedBy).HasMaxLength(DefaultConfiguration.Guid);
        builder.Property(x => x.OperationalRevision).IsConcurrencyToken();
        builder.HasIndex(x => new { x.PickingAssemblyId, x.StockKeepingUnitId });
        builder.HasOne<PickingAssembly>().WithMany().HasForeignKey(x => x.PickingAssemblyId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Warehouse>().WithMany().HasForeignKey(x => x.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StorageLocation>().WithMany().HasForeignKey(x => x.StorageLocationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StockKeepingUnit>().WithMany().HasForeignKey(x => x.StockKeepingUnitId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(t => t.HasCheckConstraint("CK_PickingEntries_Quantity",
            "[Quantity] >= 0"));
    }
}

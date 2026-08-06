using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain;

namespace Wms.Data.Configurations;

internal class InventoryTurnoverConfiguration : IEntityTypeConfiguration<InventoryTurnover>
{
    public void Configure(EntityTypeBuilder<InventoryTurnover> builder)
    {
        builder.HasKey(x => x.Id);

        builder.HasOne(x => x.StockKeepingUnit)
            .WithMany()
            .HasForeignKey(x => x.StockKeepingUnitId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Warehouse)
            .WithMany()
            .HasForeignKey(x => x.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.StorageLocation)
            .WithMany()
            .HasForeignKey(x => x.StorageLocationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new
        {
            x.RecorderType,
            x.RecorderId,
            x.RecorderLineNumber
        })
        .IsUnique();

        builder.Property(x => x.BalanceBefore)
            .HasPrecision(18, 3);

        builder.Property(x => x.QuantityDelta)
            .HasPrecision(18, 3);

        builder.Property(x => x.BalanceAfter)
            .HasPrecision(18, 3);
    }
}

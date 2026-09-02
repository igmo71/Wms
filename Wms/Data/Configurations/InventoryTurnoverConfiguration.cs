using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Common;
using Wms.Domain;

namespace Wms.Data.Configurations;

internal class InventoryTurnoverConfiguration : IEntityTypeConfiguration<InventoryTurnover>
{
    public void Configure(EntityTypeBuilder<InventoryTurnover> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.QuantityDelta)
            .HasPrecision(WarehouseQuantity.Precision, WarehouseQuantity.Scale);
        builder.Property(x => x.BalanceBefore)
            .HasPrecision(WarehouseQuantity.Precision, WarehouseQuantity.Scale);
        builder.Property(x => x.BalanceAfter)
            .HasPrecision(WarehouseQuantity.Precision, WarehouseQuantity.Scale);

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

        builder.HasOne(x => x.InventoryMovement)
            .WithMany()
            .HasForeignKey(x => x.InventoryMovementId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        builder.HasIndex(x => x.InventoryMovementId);
    }
}

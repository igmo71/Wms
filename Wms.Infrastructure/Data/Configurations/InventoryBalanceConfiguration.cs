using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Common;
using Wms.Domain;

namespace Wms.Data.Configurations;

internal class InventoryBalanceConfiguration : IEntityTypeConfiguration<InventoryBalance>
{
    public void Configure(EntityTypeBuilder<InventoryBalance> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ExpirationDate).HasColumnType("date");

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
            x.WarehouseId,
            x.StorageLocationId,
            x.StockKeepingUnitId,
            x.ExpirationDate
        })
        .IsUnique()
        .HasDatabaseName(DatabaseObjectNames.InventoryBalancesBusinessKeyIndex)
        // SQL Server's EF convention filters nullable unique keys; null is a real stock group.
        .HasFilter(null);

        builder.Property(x => x.Quantity)
            .HasPrecision(WarehouseQuantity.Precision, WarehouseQuantity.Scale);

        builder.Property(x => x.RowVersion)
            .IsRowVersion();
    }
}


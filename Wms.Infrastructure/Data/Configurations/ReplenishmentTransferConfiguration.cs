using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Common;
using Wms.Domain;

namespace Wms.Data.Configurations;

internal sealed class ReplenishmentTransferConfiguration : IEntityTypeConfiguration<ReplenishmentTransfer>
{
    public void Configure(EntityTypeBuilder<ReplenishmentTransfer> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Quantity).HasPrecision(WarehouseQuantity.Precision, WarehouseQuantity.Scale);
        builder.Property(x => x.ExpirationDate).HasColumnType("date");
        builder.Property(x => x.ConfirmedBy).HasMaxLength(DefaultConfiguration.Guid).IsRequired();
        builder.HasOne<ReplenishmentSourceCycle>().WithMany(x => x.Transfers)
            .HasForeignKey(x => x.ReplenishmentSourceCycleId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StorageLocation>().WithMany().HasForeignKey(x => x.DestinationStorageLocationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<InventoryMovement>().WithMany().HasForeignKey(x => x.InventoryMovementId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.InventoryMovementId).IsUnique()
            .HasDatabaseName(DatabaseObjectNames.ReplenishmentTransferMovementIndex);
        builder.ToTable(t => t.HasCheckConstraint("CK_ReplenishmentTransfers_Quantity", "[Quantity] > 0"));
    }
}

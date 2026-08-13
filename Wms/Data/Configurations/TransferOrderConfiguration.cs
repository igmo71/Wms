using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Data.Configurations;

internal class TransferOrderConfiguration : IEntityTypeConfiguration<TransferOrder>
{
    public void Configure(EntityTypeBuilder<TransferOrder> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Number).HasMaxLength(DefaultConfiguration.Code).IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(DefaultConfiguration.Guid).IsRequired();
        builder.Property(x => x.StartedBy).HasMaxLength(DefaultConfiguration.Guid);
        builder.Property(x => x.CompletedBy).HasMaxLength(DefaultConfiguration.Guid);
        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.HasOne(x => x.Warehouse)
            .WithMany()
            .HasForeignKey(x => x.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        builder.HasOne(x => x.TransitStorageLocation)
            .WithMany()
            .HasForeignKey(x => x.TransitStorageLocationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.Number);
        builder.HasIndex(x => new { x.WarehouseId, x.Date });
        builder.HasIndex(x => x.TransitStorageLocationId)
            .IsUnique()
            .HasFilter($"[TransitStorageLocationId] IS NOT NULL AND [Status] IN ({(int)TransferOrderStatus.Draft}, {(int)TransferOrderStatus.InProgress})");
    }
}

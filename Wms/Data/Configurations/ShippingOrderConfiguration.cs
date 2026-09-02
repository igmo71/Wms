using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain;

namespace Wms.Data.Configurations;

internal class ShippingOrderConfiguration : IEntityTypeConfiguration<ShippingOrder>
{
    public void Configure(EntityTypeBuilder<ShippingOrder> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.OperationalRevision).IsConcurrencyToken();

        builder.Property(x => x.Number).HasMaxLength(DefaultConfiguration.Code);
        builder.Property(x => x.Comment).HasMaxLength(DefaultConfiguration.Description);
        builder.Property(x => x.PickingStartedBy).HasMaxLength(DefaultConfiguration.Guid);
        builder.Property(x => x.ReadyForShipmentBy).HasMaxLength(DefaultConfiguration.Guid);
        builder.Property(x => x.ShippedBy).HasMaxLength(DefaultConfiguration.Guid);
        builder.Property(x => x.RolledBackBy).HasMaxLength(DefaultConfiguration.Guid);
        builder.Property(x => x.RollbackReason).HasMaxLength(DefaultConfiguration.Description);
        builder.Property(x => x.ExternalSynchronizationFingerprint)
            .HasMaxLength(DefaultConfiguration.Fingerprint);
        builder.Property(x => x.ExternalSynchronizationAcknowledgedFingerprint)
            .HasMaxLength(DefaultConfiguration.Fingerprint);
        builder.Property(x => x.ExternalSynchronizationAcknowledgedBy)
            .HasMaxLength(DefaultConfiguration.Guid);
        builder.Ignore(x => x.ExternalChangeDetected);
        builder.Ignore(x => x.Receiver);

        builder.HasOne(x => x.Warehouse).WithMany()
            .HasForeignKey(x => x.WarehouseId).HasPrincipalKey(x => x.Id)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ShippingLocation).WithMany()
            .HasForeignKey(x => x.ShippingLocationId).HasPrincipalKey(x => x.Id)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.DeliveryDirection).WithMany()
            .HasForeignKey(x => x.DeliveryDirectionId).HasPrincipalKey(x => x.Id)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Navigation(x => x.Items)
            .HasField("_items")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Navigation(x => x.BaseItems)
            .HasField("_baseItems")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

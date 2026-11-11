using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain;

namespace Wms.Data.Configurations;

internal sealed class ShippingExchangeTargetConfiguration
    : IEntityTypeConfiguration<ShippingExchangeTarget>
{
    public void Configure(EntityTypeBuilder<ShippingExchangeTarget> builder)
    {
        builder.HasKey(x => new { x.PickingAssemblyId, x.ShippingOrderId });
        builder.Property(x => x.ReviewedSourceJson).IsRequired();
        builder.Property(x => x.TargetDocumentJson).IsRequired();
        builder.Property(x => x.LastError).HasMaxLength(2000);
        builder.Property(x => x.OperationalRevision).IsConcurrencyToken();
        builder.HasIndex(x => new { x.Stage, x.LastAttemptAtUtc });
        builder.HasOne<PickingAssembly>().WithMany().HasForeignKey(x => x.PickingAssemblyId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ShippingOrder>().WithMany().HasForeignKey(x => x.ShippingOrderId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

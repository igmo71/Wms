using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain;

namespace Wms.Data.Configurations;

internal sealed class PickingAssemblySourceLinkConfiguration
    : IEntityTypeConfiguration<PickingAssemblySourceLink>
{
    public void Configure(EntityTypeBuilder<PickingAssemblySourceLink> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.PlanSourceSnapshotJson).IsRequired();
        builder.Property(x => x.AttachedBy).HasMaxLength(DefaultConfiguration.Guid).IsRequired();
        builder.Property(x => x.DetachedBy).HasMaxLength(DefaultConfiguration.Guid);
        builder.Property(x => x.LastStartError).HasMaxLength(2000);
        builder.Property(x => x.LastReturnError).HasMaxLength(2000);
        builder.HasIndex(x => x.ShippingOrderId).IsUnique()
            .HasFilter("[DetachedAtUtc] IS NULL")
            .HasDatabaseName(DatabaseObjectNames.PickingAssemblyActiveSourceIndex);
        builder.HasIndex(x => x.PickingAssemblyId);
        builder.HasOne(x => x.ShippingOrder).WithMany()
            .HasForeignKey(x => x.ShippingOrderId).OnDelete(DeleteBehavior.Restrict);
    }
}

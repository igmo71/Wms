using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain;

namespace Wms.Data.Configurations;

internal class ReceivingOrderConfiguration : IEntityTypeConfiguration<ReceivingOrder>
{
    public void Configure(EntityTypeBuilder<ReceivingOrder> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Number).HasMaxLength(DefaultConfiguration.Code);
        builder.Property(x => x.Comment).HasMaxLength(DefaultConfiguration.Description);
        builder.Property(x => x.SenderType).HasMaxLength(DefaultConfiguration.Name);
        builder.Property(x => x.StartedBy).HasMaxLength(DefaultConfiguration.Guid);
        builder.Property(x => x.CompletedBy).HasMaxLength(DefaultConfiguration.Guid);
        builder.Property(x => x.BaseOrderType).HasMaxLength(DefaultConfiguration.Name);

        builder.HasOne(X => X.Warehouse).WithMany()
            .HasForeignKey(X => X.WarehouseId).HasPrincipalKey(X => X.Id)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(X => X.ReceivingLocation).WithMany()
            .HasForeignKey(X => X.ReceivingLocationId).HasPrincipalKey(X => X.Id)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

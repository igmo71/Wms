using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain;

namespace Wms.Data.Configurations;

internal class ShippingOrderItemConfiguration : IEntityTypeConfiguration<ShippingOrderItem>
{
    public void Configure(EntityTypeBuilder<ShippingOrderItem> builder)
    {
        builder.HasKey(x => new { x.ShippingOrderId, x.LineNumber });

        builder.Property(x => x.Comment).HasMaxLength(DefaultConfiguration.Description);

        builder.HasOne(x => x.ShippingOrder).WithMany(x => x.Items)
            .HasForeignKey(x => x.ShippingOrderId).HasPrincipalKey(x => x.Id)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.StockKeepingUnit).WithMany()
            .HasForeignKey(x => x.StockKeepingUnitId).HasPrincipalKey(x => x.Id)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

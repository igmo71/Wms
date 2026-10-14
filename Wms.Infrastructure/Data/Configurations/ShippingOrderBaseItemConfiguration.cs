using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Common;
using Wms.Domain;

namespace Wms.Data.Configurations;

internal class ShippingOrderBaseItemConfiguration : IEntityTypeConfiguration<ShippingOrderBaseItem>
{
    public void Configure(EntityTypeBuilder<ShippingOrderBaseItem> builder)
    {
        builder.HasKey(x => new { x.ShippingOrderId, x.LineNumber });

        builder.Property(x => x.BaseOrderType).HasMaxLength(DefaultConfiguration.Name);
        builder.Property(x => x.PlanQuantity)
            .HasPrecision(WarehouseQuantity.Precision, WarehouseQuantity.Scale);

        builder.HasOne(x => x.ShippingOrder).WithMany(x => x.BaseItems)
            .HasForeignKey(x => x.ShippingOrderId).HasPrincipalKey(x => x.Id)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.StockKeepingUnit).WithMany()
            .HasForeignKey(x => x.StockKeepingUnitId).HasPrincipalKey(x => x.Id)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

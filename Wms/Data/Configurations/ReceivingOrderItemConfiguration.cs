using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Common;
using Wms.Domain;

namespace Wms.Data.Configurations;

internal class ReceivingOrderItemConfiguration : IEntityTypeConfiguration<ReceivingOrderItem>
{
    public void Configure(EntityTypeBuilder<ReceivingOrderItem> builder)
    {
        builder.HasKey(x => new { x.ReceivingOrderId, x.LineNumber });

        builder.Property(x => x.Comment).HasMaxLength(DefaultConfiguration.Description);
        builder.Property(x => x.PlanQuantity)
            .HasPrecision(WarehouseQuantity.Precision, WarehouseQuantity.Scale);
        builder.Property(x => x.FactQuantity)
            .HasPrecision(WarehouseQuantity.Precision, WarehouseQuantity.Scale);

        builder.HasOne(x => x.ReceivingOrder).WithMany(x => x.Items)
            .HasForeignKey(x => x.ReceivingOrderId).HasPrincipalKey(x => x.Id)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.StockKeepingUnit).WithMany()
            .HasForeignKey(x => x.StockKeepingUnitId).HasPrincipalKey(x => x.Id)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

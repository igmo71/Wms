using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Common;
using Wms.Domain;

namespace Wms.Data.Configurations;

internal sealed class PickingAssemblyPlanLineConfiguration
    : IEntityTypeConfiguration<PickingAssemblyPlanLine>
{
    public void Configure(EntityTypeBuilder<PickingAssemblyPlanLine> builder)
    {
        builder.HasKey(x => new { x.PickingAssemblyId, x.ShippingOrderId, x.StockKeepingUnitId });
        builder.Property(x => x.Quantity)
            .HasPrecision(WarehouseQuantity.Precision, WarehouseQuantity.Scale);
        builder.HasOne<ShippingOrder>().WithMany().HasForeignKey(x => x.ShippingOrderId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.StockKeepingUnit).WithMany()
            .HasForeignKey(x => x.StockKeepingUnitId).OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(t => t.HasCheckConstraint("CK_PickingAssemblyPlanLines_Quantity",
            "[Quantity] >= 0"));
    }
}

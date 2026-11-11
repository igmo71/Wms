using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Common;
using Wms.Domain;

namespace Wms.Data.Configurations;

internal sealed class PickingAllocationConfiguration : IEntityTypeConfiguration<PickingAllocation>
{
    public void Configure(EntityTypeBuilder<PickingAllocation> builder)
    {
        builder.HasKey(x => new { x.PickingAssemblyId, x.ShippingOrderId,
            x.StockKeepingUnitId });
        builder.Property(x => x.Quantity)
            .HasPrecision(WarehouseQuantity.Precision, WarehouseQuantity.Scale);
        builder.HasOne<PickingAssembly>().WithMany().HasForeignKey(x => x.PickingAssemblyId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ShippingOrder>().WithMany().HasForeignKey(x => x.ShippingOrderId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StockKeepingUnit>().WithMany().HasForeignKey(x => x.StockKeepingUnitId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(t => t.HasCheckConstraint("CK_PickingAllocations_Quantity",
            "[Quantity] >= 0"));
    }
}

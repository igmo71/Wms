using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain;

namespace Wms.Data.Configurations;

internal class ReceivingOrderItemConfiguration : IEntityTypeConfiguration<ReceivingOrderItem>
{
    public void Configure(EntityTypeBuilder<ReceivingOrderItem> builder)
    {
        builder.HasKey(x => new { x.ReceivingOrderId, x.LineNumber });

        builder.HasOne(x => x.ReceivingOrder).WithMany(x => x.Items)
            .HasForeignKey(x => x.ReceivingOrderId).HasPrincipalKey(x => x.Id)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

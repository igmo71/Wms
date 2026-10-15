using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain;

namespace Wms.Data.Configurations;

internal sealed class ReceivingOrderSourceLinkConfiguration
    : IEntityTypeConfiguration<ReceivingOrderSourceLink>
{
    public void Configure(EntityTypeBuilder<ReceivingOrderSourceLink> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.ReceivingSourceDocumentId)
            .IsUnique()
            .HasFilter("[DetachedAtUtc] IS NULL");
        builder.HasIndex(x => x.ReceivingOrderId);
        builder.HasOne(x => x.ReceivingOrder).WithMany(x => x.SourceLinks)
            .HasForeignKey(x => x.ReceivingOrderId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ReceivingSourceDocument).WithMany()
            .HasForeignKey(x => x.ReceivingSourceDocumentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

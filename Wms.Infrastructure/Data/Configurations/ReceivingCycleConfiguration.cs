using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain;

namespace Wms.Data.Configurations;

internal sealed class ReceivingCycleConfiguration : IEntityTypeConfiguration<ReceivingCycle>
{
    public void Configure(EntityTypeBuilder<ReceivingCycle> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.ReceivingOrderId, x.Number }).IsUnique();
        builder.Property(x => x.CompletedBy).HasMaxLength(DefaultConfiguration.Guid);
        builder.HasOne(x => x.ReceivingOrder).WithMany(x => x.Cycles)
            .HasForeignKey(x => x.ReceivingOrderId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ReceivingLocation).WithMany()
            .HasForeignKey(x => x.ReceivingLocationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(x => x.SourceVersions)
            .HasField("_sourceVersions")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class ReceivingCycleSourceVersionConfiguration
    : IEntityTypeConfiguration<ReceivingCycleSourceVersion>
{
    public void Configure(EntityTypeBuilder<ReceivingCycleSourceVersion> builder)
    {
        builder.HasKey(x => new { x.ReceivingCycleId, x.ReceivingSourceDocumentId });
        builder.HasOne(x => x.ReceivingCycle).WithMany(x => x.SourceVersions)
            .HasForeignKey(x => x.ReceivingCycleId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ReceivingSourceDocument).WithMany()
            .HasForeignKey(x => x.ReceivingSourceDocumentId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ReceivingSourceVersion).WithMany()
            .HasForeignKey(x => new { x.ReceivingSourceVersionId, x.ReceivingSourceDocumentId })
            .HasPrincipalKey(x => new { x.Id, x.ReceivingSourceDocumentId })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

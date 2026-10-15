using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Common;
using Wms.Domain;

namespace Wms.Data.Configurations;

internal sealed class ReceivingSourceVersionConfiguration
    : IEntityTypeConfiguration<ReceivingSourceVersion>
{
    public void Configure(EntityTypeBuilder<ReceivingSourceVersion> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.Id, x.ReceivingSourceDocumentId });
        builder.Property(x => x.ContentFingerprint)
            .HasMaxLength(DefaultConfiguration.Fingerprint).IsRequired();
        builder.Property(x => x.SnapshotJson).IsRequired();
        builder.HasIndex(x => new { x.ReceivingSourceDocumentId, x.ReceivedAtUtc });
        builder.HasOne(x => x.ReceivingSourceDocument).WithMany()
            .HasForeignKey(x => x.ReceivingSourceDocumentId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(x => x.Lines)
            .HasField("_lines")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class ReceivingSourceLineProjectionConfiguration
    : IEntityTypeConfiguration<ReceivingSourceLineProjection>
{
    public void Configure(EntityTypeBuilder<ReceivingSourceLineProjection> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Quantity)
            .HasPrecision(WarehouseQuantity.Precision, WarehouseQuantity.Scale);
        builder.HasIndex(x => new { x.ReceivingSourceVersionId, x.LineNumber });
        builder.HasOne(x => x.ReceivingSourceVersion).WithMany(x => x.Lines)
            .HasForeignKey(x => x.ReceivingSourceVersionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.StockKeepingUnit).WithMany()
            .HasForeignKey(x => x.StockKeepingUnitId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

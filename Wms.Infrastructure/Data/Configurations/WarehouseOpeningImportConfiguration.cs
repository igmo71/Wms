using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain;

namespace Wms.Data.Configurations;

internal sealed class WarehouseOpeningImportConfiguration
    : IEntityTypeConfiguration<WarehouseOpeningImport>
{
    public void Configure(EntityTypeBuilder<WarehouseOpeningImport> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.WarehouseId).IsUnique();
        builder.HasOne(x => x.Warehouse).WithMany()
            .HasForeignKey(x => x.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(x => x.SourceName)
            .HasMaxLength(WarehouseOpeningImport.MaximumSourceNameLength).IsRequired();
        builder.Property(x => x.SourceSha256)
            .HasMaxLength(DefaultConfiguration.Fingerprint).IsUnicode(false).IsRequired();
        builder.Property(x => x.Status).HasConversion<int>().IsRequired();
        builder.Property(x => x.TopologyImportedBy)
            .HasMaxLength(DefaultConfiguration.Guid).IsRequired();
        builder.Property(x => x.StockImportedBy).HasMaxLength(DefaultConfiguration.Guid);
        builder.Property(x => x.TopologySummary)
            .HasMaxLength(WarehouseOpeningImport.MaximumSummaryLength).IsRequired();
        builder.Property(x => x.StockSummary)
            .HasMaxLength(WarehouseOpeningImport.MaximumSummaryLength);
        builder.Property(x => x.Revision).IsConcurrencyToken();
    }
}

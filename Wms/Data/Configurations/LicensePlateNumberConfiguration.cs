using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain;

namespace Wms.Data.Configurations;

public sealed class LicensePlateNumberConfiguration : IEntityTypeConfiguration<LicensePlateNumber>, IEntityTypeConfiguration<LicensePlateNumberBatch>
{
    public void Configure(EntityTypeBuilder<LicensePlateNumber> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).HasMaxLength(15).IsUnicode(false).IsRequired()
            .HasDefaultValueSql("'LPN' + RIGHT('000000000000' + CONVERT(varchar(12), NEXT VALUE FOR [LicensePlateNumberSequence]), 12)");
        builder.HasIndex(x => x.Code).IsUnique();
        builder.HasOne(x => x.Batch).WithMany(x => x.Labels).HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.Restrict);
    }

    public void Configure(EntityTypeBuilder<LicensePlateNumberBatch> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.IssuedBy).HasMaxLength(450).IsRequired();
        builder.HasIndex(x => x.IssuedAtUtc);
    }
}

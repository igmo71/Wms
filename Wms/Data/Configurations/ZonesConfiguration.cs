using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain;

namespace Wms.Data.Configurations;

internal class ZonesConfiguration : IEntityTypeConfiguration<Zone>
{
    public void Configure(EntityTypeBuilder<Zone> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Code).HasMaxLength(DefaultConfiguration.Code).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(DefaultConfiguration.Name);
        builder.Property(x => x.Type).HasConversion<int>();

        builder.HasOne(x => x.Warehouse).WithMany(x => x.Zones)
            .HasForeignKey(x => x.WarehouseId).HasPrincipalKey(x => x.Id)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.WarehouseId, x.Code }).IsUnique();
    }
}

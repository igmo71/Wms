using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain;

namespace Wms.Data.Configurations;

internal class StorageLocationConfiguration : IEntityTypeConfiguration<StorageLocation>
{
    public void Configure(EntityTypeBuilder<StorageLocation> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Code).HasMaxLength(DefaultConfiguration.Code).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(DefaultConfiguration.Name);

        builder.HasOne(x => x.Warehouse).WithMany(x => x.StorageLocations)
            .HasForeignKey(x => x.WarehouseId).HasPrincipalKey(x => x.Id)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Zone).WithMany(x => x.StorageLocations)
            .HasForeignKey(x => x.ZoneId).HasPrincipalKey(x => x.Id)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Parent).WithMany(x => x.Children)
            .HasForeignKey(x => x.ParentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.OwnsOne(x => x.Dimensions);
        builder.OwnsOne(x => x.Coordinates);

        builder.HasIndex(x => new { x.ZoneId, x.Code }).IsUnique();
        builder.HasIndex(x => x.ParentId);
    }
}

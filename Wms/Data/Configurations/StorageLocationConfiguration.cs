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
        builder.Property(x => x.Name).HasMaxLength(DefaultConfiguration.Name).IsRequired();

        builder.HasOne(x => x.Warehouse).WithMany(x => x.StorageLocations)
            .HasForeignKey(x => x.WarehouseId).HasPrincipalKey(x => x.Id)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.Children).WithOne(x => x.Parent)
            .HasForeignKey(x => x.ParentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Navigation(x => x.Children)
            .HasField("_children")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.OwnsOne(x => x.Dimensions);
        builder.OwnsOne(x => x.Coordinates);

        builder.HasIndex(x => new { x.ZoneId, x.Code }).IsUnique();
        builder.HasIndex(x => x.ParentId);
    }
}

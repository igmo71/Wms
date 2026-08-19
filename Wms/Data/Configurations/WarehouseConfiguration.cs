using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain;

namespace Wms.Data.Configurations;

internal class WarehouseConfiguration : IEntityTypeConfiguration<Warehouse>
{
    public void Configure(EntityTypeBuilder<Warehouse> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name).HasMaxLength(DefaultConfiguration.Name);

        builder.Navigation(x => x.Zones)
            .HasField("_zones")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Navigation(x => x.StorageLocations)
            .HasField("_storageLocations")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain;

namespace Wms.Data.Configurations;

internal class InventoryCountConfiguration : IEntityTypeConfiguration<InventoryCount>
{
    public void Configure(EntityTypeBuilder<InventoryCount> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Number).HasMaxLength(DefaultConfiguration.Code).IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(DefaultConfiguration.Guid).IsRequired();
        builder.Property(x => x.UpdatedBy).HasMaxLength(DefaultConfiguration.Guid);
        builder.Property(x => x.PostedBy).HasMaxLength(DefaultConfiguration.Guid);

        builder.HasOne(x => x.Warehouse)
            .WithMany()
            .HasForeignKey(x => x.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        builder.Navigation(x => x.Items)
            .HasField("_items")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(x => x.Number);
        builder.HasIndex(x => new { x.WarehouseId, x.Date });
    }
}

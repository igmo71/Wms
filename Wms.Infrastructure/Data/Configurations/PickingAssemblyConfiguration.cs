using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain;

namespace Wms.Data.Configurations;

internal sealed class PickingAssemblyConfiguration : IEntityTypeConfiguration<PickingAssembly>
{
    public void Configure(EntityTypeBuilder<PickingAssembly> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Number).HasMaxLength(13).IsRequired();
        builder.HasIndex(x => x.Number).IsUnique();
        builder.Property(x => x.CreatedBy).HasMaxLength(DefaultConfiguration.Guid).IsRequired();
        builder.Property(x => x.ResponsibleUserId).HasMaxLength(DefaultConfiguration.Guid).IsRequired();
        builder.Property(x => x.CancelledBy).HasMaxLength(DefaultConfiguration.Guid);
        builder.Property(x => x.OperationalRevision).IsConcurrencyToken();
        builder.HasIndex(x => new { x.ResponsibleUserId, x.Status });
        builder.HasOne(x => x.Warehouse).WithMany().HasForeignKey(x => x.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.SourceLinks).WithOne(x => x.PickingAssembly)
            .HasForeignKey(x => x.PickingAssemblyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.PlanLines).WithOne(x => x.PickingAssembly)
            .HasForeignKey(x => x.PickingAssemblyId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.SourceLinks).HasField("_sourceLinks")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(x => x.PlanLines).HasField("_planLines")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Ignore(x => x.ActiveSources);
        builder.ToTable(t => t.HasCheckConstraint("CK_PickingAssemblies_NumberFormat",
            "LEN([Number]) = 13 AND [Number] LIKE '[0-9][0-9][0-9][0-9][0-9][0-9]-[0-9][0-9][0-9][0-9][0-9][0-9]'"));
    }
}

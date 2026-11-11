using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain;

namespace Wms.Data.Configurations;

internal sealed class PickingClosureConfiguration : IEntityTypeConfiguration<PickingClosure>
{
    public void Configure(EntityTypeBuilder<PickingClosure> builder)
    {
        builder.HasKey(x => x.PickingAssemblyId);
        builder.Property(x => x.ClosedBy).HasMaxLength(DefaultConfiguration.Guid).IsRequired();
        builder.Property(x => x.DecisionReason).HasMaxLength(2000);
        builder.Property(x => x.ReviewedSourcesJson).IsRequired();
        builder.Property(x => x.MovementIdsJson).IsRequired();
        builder.HasOne<PickingAssembly>().WithOne()
            .HasForeignKey<PickingClosure>(x => x.PickingAssemblyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain;

namespace Wms.Data.Configurations;

internal class DeliveryDirectionCobfiguration : IEntityTypeConfiguration<DeliveryDirection>
{
    public void Configure(EntityTypeBuilder<DeliveryDirection> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Description).HasMaxLength(DefaultConfiguration.Name);

        builder.Property(x => x.Comment).HasMaxLength(DefaultConfiguration.Name);
    }
}

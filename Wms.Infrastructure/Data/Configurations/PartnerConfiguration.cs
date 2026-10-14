using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain;

namespace Wms.Data.Configurations;

internal class PartnerConfiguration : IEntityTypeConfiguration<Partner>
{
    public void Configure(EntityTypeBuilder<Partner> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Code).HasMaxLength(DefaultConfiguration.Code);
        builder.Property(x => x.Name).HasMaxLength(DefaultConfiguration.Name);
    }
}

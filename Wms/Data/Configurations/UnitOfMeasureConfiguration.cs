using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain;

namespace Wms.Data.Configurations;

public class UnitOfMeasureConfiguration : IEntityTypeConfiguration<UnitOfMeasure>
{
    public void Configure(EntityTypeBuilder<UnitOfMeasure> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Code).HasMaxLength(DefaultConfiguration.Code);
        builder.Property(x => x.Name).HasMaxLength(DefaultConfiguration.Name);
        builder.Property(x => x.Abbreviation).HasMaxLength(DefaultConfiguration.Code);
        builder.Property(x => x.Description).HasMaxLength(DefaultConfiguration.Description);
    }
}

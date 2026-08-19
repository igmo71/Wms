using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain;

namespace Wms.Data.Configurations;

public class StockKeepingUnitConfiguration : IEntityTypeConfiguration<StockKeepingUnit>
{
    public void Configure(EntityTypeBuilder<StockKeepingUnit> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Code).HasMaxLength(DefaultConfiguration.Code);
        builder.Property(x => x.Name).HasMaxLength(DefaultConfiguration.Name);

        builder.HasOne(x => x.BaseUnitOfMeasure).WithMany()
            .HasForeignKey(x => x.BaseUnitOfMeasureId).HasPrincipalKey(x => x.Id)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Navigation(x => x.Barcodes)
            .HasField("_barcodes")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

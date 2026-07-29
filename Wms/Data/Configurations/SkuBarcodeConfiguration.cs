using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain;

namespace Wms.Data.Configurations;

internal class SkuBarcodeConfiguration : IEntityTypeConfiguration<SkuBarcode>
{
    public void Configure(EntityTypeBuilder<SkuBarcode> builder)
    {
        builder.HasKey(x => new { x.SkuId, x.Value });

        builder.Property(x => x.Value).HasMaxLength(DefaultConfiguration.Code);

        builder.HasOne(x => x.Sku).WithMany(x => x.Barcodes)
            .HasForeignKey(x => x.SkuId).HasPrincipalKey(x => x.Id)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

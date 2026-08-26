using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Wms.Data.Configurations;

internal sealed class MobileCommandReceiptConfiguration
    : IEntityTypeConfiguration<MobileCommandReceipt>
{
    public void Configure(EntityTypeBuilder<MobileCommandReceipt> builder)
    {
        builder.HasKey(x => new
        {
            x.UserId,
            x.CommandType,
            x.ClientRequestId
        });

        builder.Property(x => x.UserId)
            .HasMaxLength(DefaultConfiguration.Guid)
            .IsRequired();
        builder.Property(x => x.CommandType)
            .HasMaxLength(100)
            .IsRequired();
        builder.Property(x => x.RequestHash)
            .HasMaxLength(64)
            .IsFixedLength()
            .IsRequired();
    }
}

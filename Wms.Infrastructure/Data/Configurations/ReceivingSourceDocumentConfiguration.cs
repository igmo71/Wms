using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain;

namespace Wms.Data.Configurations;

internal sealed class ReceivingSourceDocumentConfiguration
    : IEntityTypeConfiguration<ReceivingSourceDocument>
{
    public void Configure(EntityTypeBuilder<ReceivingSourceDocument> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.RefKey).IsUnique();
        builder.ToTable(table => table.HasCheckConstraint(
            "CK_ReceivingSourceDocuments_RefKey_NotEmpty",
            "[RefKey] <> '00000000-0000-0000-0000-000000000000'"));
        builder.HasIndex(x => x.LastSuccessfulVersionId);
        builder.Property(x => x.LastSuccessfulVersionId).IsConcurrencyToken();
        builder.Property(x => x.LastSuccessfulCheckAtUtc).IsConcurrencyToken();
    }
}

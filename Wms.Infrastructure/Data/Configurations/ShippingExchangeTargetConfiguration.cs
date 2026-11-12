using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain;

namespace Wms.Data.Configurations;

internal sealed class ShippingExchangeTargetConfiguration
    : IEntityTypeConfiguration<ShippingExchangeTarget>
{
    public void Configure(EntityTypeBuilder<ShippingExchangeTarget> builder)
    {
        builder.HasKey(x => new { x.PickingAssemblyId, x.ShippingOrderId });
        builder.Property(x => x.ReviewedSourceJson).IsRequired();
        builder.Property(x => x.TargetDocumentJson).IsRequired();
        builder.Property(x => x.LastError).HasMaxLength(ShippingExchangeTarget.MaximumErrorLength);
        builder.Property(x => x.OperationalRevision).IsConcurrencyToken();
        builder.HasIndex(x => new { x.Stage, x.LastAttemptAtUtc });
        builder.HasOne<PickingAssembly>().WithMany().HasForeignKey(x => x.PickingAssemblyId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ShippingOrder>().WithMany().HasForeignKey(x => x.ShippingOrderId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(table => table.HasCheckConstraint(
            "CK_ShippingExchangeTargets_Stage",
            "[DecisionRevision] >= 0 AND [OperationalRevision] >= 0 AND "
            + "(([Stage] = 0 AND [PatchConfirmedAtUtc] IS NULL "
            + "AND [PostConfirmedAtUtc] IS NULL) OR "
            + "([Stage] = 1 AND [PatchConfirmedAtUtc] IS NOT NULL "
            + "AND [PostConfirmedAtUtc] IS NULL AND [LastAttemptAtUtc] IS NOT NULL) OR "
            + "([Stage] = 2 AND [PatchConfirmedAtUtc] IS NOT NULL "
            + "AND [PostConfirmedAtUtc] IS NOT NULL "
            + "AND [PostConfirmedAtUtc] >= [PatchConfirmedAtUtc] "
            + "AND [LastError] IS NULL) OR "
            + "([Stage] = 3 AND [PostConfirmedAtUtc] IS NULL "
            + "AND [LastAttemptAtUtc] IS NOT NULL AND [LastError] IS NOT NULL))"));
    }
}

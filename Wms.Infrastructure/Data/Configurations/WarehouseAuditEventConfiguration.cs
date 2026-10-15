using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain;

namespace Wms.Data.Configurations;

internal sealed class WarehouseAuditEventConfiguration
    : IEntityTypeConfiguration<WarehouseAuditEvent>
{
    public void Configure(EntityTypeBuilder<WarehouseAuditEvent> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.EventType).HasMaxLength(100).IsRequired();
        builder.Property(x => x.ActorUserId).HasMaxLength(DefaultConfiguration.Guid);
        builder.Property(x => x.ActionSource).HasMaxLength(100);
        builder.Property(x => x.BeforeValuesJson).HasMaxLength(4000);
        builder.Property(x => x.AfterValuesJson).HasMaxLength(4000);
        builder.Property(x => x.MovementIdsJson).IsRequired();
        builder.Property(x => x.SourceVersionIdsJson).IsRequired();
        builder.Property(x => x.CommandUserId).HasMaxLength(DefaultConfiguration.Guid);
        builder.Property(x => x.CommandType).HasMaxLength(100);

        builder.HasOne<CommandReceipt>().WithMany(x => x.AuditEvents)
            .HasForeignKey(x => new { x.CommandUserId, x.CommandType, x.RequestId })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ReceivingOrder>().WithMany()
            .HasForeignKey(x => x.ReceivingOrderId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<LicensePlateNumber>().WithMany()
            .HasForeignKey(x => x.LicensePlateNumberId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StockKeepingUnit>().WithMany()
            .HasForeignKey(x => x.StockKeepingUnitId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ReceivingSourceDocument>().WithMany()
            .HasForeignKey(x => x.ReceivingSourceDocumentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.ReceivingOrderId, x.OccurredAtUtc });
        builder.HasIndex(x => new { x.LicensePlateNumberId, x.OccurredAtUtc });
        builder.HasIndex(x => new { x.StockKeepingUnitId, x.OccurredAtUtc });
        builder.HasIndex(x => new { x.ReceivingSourceDocumentId, x.OccurredAtUtc });
    }
}

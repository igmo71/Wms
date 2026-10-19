using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain;

namespace Wms.Data.Configurations;

internal sealed class ReceivingOrderParticipantConfiguration
    : IEntityTypeConfiguration<ReceivingOrderParticipant>
{
    public void Configure(EntityTypeBuilder<ReceivingOrderParticipant> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.UserId).HasMaxLength(450).IsRequired();
        builder.HasIndex(x => new { x.ReceivingCycleId, x.UserId })
            .IsUnique();
        builder.HasOne(x => x.ReceivingOrder)
            .WithMany(x => x.Participants)
            .HasForeignKey(x => x.ReceivingOrderId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.ReceivingCycle).WithMany()
            .HasForeignKey(x => x.ReceivingCycleId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

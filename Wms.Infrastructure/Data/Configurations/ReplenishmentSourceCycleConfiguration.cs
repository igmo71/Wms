using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain;

namespace Wms.Data.Configurations;

internal sealed class ReplenishmentSourceCycleConfiguration : IEntityTypeConfiguration<ReplenishmentSourceCycle>
{
    public void Configure(EntityTypeBuilder<ReplenishmentSourceCycle> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.StartedBy).HasMaxLength(DefaultConfiguration.Guid).IsRequired();
        builder.HasOne<ReplenishmentTask>().WithMany(x => x.SourceCycles)
            .HasForeignKey(x => x.ReplenishmentTaskId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StorageLocation>().WithMany().HasForeignKey(x => x.SourceStorageLocationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.ReplenishmentTaskId).IsUnique()
            .HasFilter("[CompletedAtUtc] IS NULL")
            .HasDatabaseName(DatabaseObjectNames.ReplenishmentActiveSourceIndex);
        builder.Navigation(x => x.Transfers).HasField("_transfers")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

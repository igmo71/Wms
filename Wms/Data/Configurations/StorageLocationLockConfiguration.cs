using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain;

namespace Wms.Data.Configurations;

internal sealed class StorageLocationLockConfiguration
    : IEntityTypeConfiguration<StorageLocationLock>
{
    public void Configure(EntityTypeBuilder<StorageLocationLock> builder)
    {
        builder.HasKey(x => x.StorageLocationId)
            .HasName(DatabaseObjectNames.StorageLocationLocksPrimaryKey);

        builder.Property(x => x.Reason)
            .HasMaxLength(StorageLocationLock.MaximumReasonLength)
            .IsRequired();
        builder.Property(x => x.LockedBy)
            .HasMaxLength(DefaultConfiguration.Guid)
            .IsRequired();

        builder.HasOne(x => x.StorageLocation)
            .WithOne(x => x.ActiveLock)
            .HasForeignKey<StorageLocationLock>(x => x.StorageLocationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

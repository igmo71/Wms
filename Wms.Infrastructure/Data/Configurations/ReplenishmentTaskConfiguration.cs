using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Common;
using Wms.Domain;

namespace Wms.Data.Configurations;

internal sealed class ReplenishmentTaskConfiguration : IEntityTypeConfiguration<ReplenishmentTask>
{
    public void Configure(EntityTypeBuilder<ReplenishmentTask> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.OrderedQuantity).HasPrecision(WarehouseQuantity.Precision, WarehouseQuantity.Scale);
        builder.Property(x => x.TransferredQuantity).HasPrecision(WarehouseQuantity.Precision, WarehouseQuantity.Scale);
        builder.Ignore(x => x.RemainingQuantity);
        builder.Property(x => x.OrderedBy).HasMaxLength(DefaultConfiguration.Guid).IsRequired();
        builder.Property(x => x.AssignedTo).HasMaxLength(DefaultConfiguration.Guid);
        builder.Property(x => x.CancelledBy).HasMaxLength(DefaultConfiguration.Guid);
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasIndex(x => x.PickingAssemblyId);
        builder.HasOne<PickingAssembly>().WithMany().HasForeignKey(x => x.PickingAssemblyId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.WarehouseId, x.Status, x.OrderedAtUtc });
        builder.HasOne<Warehouse>().WithMany().HasForeignKey(x => x.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StockKeepingUnit>().WithMany().HasForeignKey(x => x.StockKeepingUnitId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StorageLocation>().WithMany().HasForeignKey(x => x.RecommendedSourceStorageLocationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StorageLocation>().WithMany().HasForeignKey(x => x.RecommendedDestinationStorageLocationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(x => x.SourceCycles).HasField("_sourceCycles")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.ToTable(t => t.HasCheckConstraint("CK_ReplenishmentTasks_Quantity",
            "[OrderedQuantity] > 0 AND [TransferredQuantity] >= 0 AND [TransferredQuantity] <= [OrderedQuantity]"));
    }
}

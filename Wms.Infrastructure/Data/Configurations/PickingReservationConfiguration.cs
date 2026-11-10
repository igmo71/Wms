using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Common;
using Wms.Domain;

namespace Wms.Data.Configurations;

internal sealed class PickingReservationConfiguration : IEntityTypeConfiguration<PickingReservation>
{
    public void Configure(EntityTypeBuilder<PickingReservation> builder)
    {
        builder.HasKey(x => x.PickingEntryId);
        builder.Property(x => x.Quantity).HasPrecision(WarehouseQuantity.Precision, WarehouseQuantity.Scale);
        builder.Property(x => x.ExpirationDate).HasColumnType("date");
        builder.Property(x => x.CreatedBy).HasMaxLength(DefaultConfiguration.Guid).IsRequired();
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasIndex(x => x.PickingAssemblyId);
        builder.HasOne<PickingAssembly>().WithMany().HasForeignKey(x => x.PickingAssemblyId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new
        {
            x.WarehouseId, x.StorageLocationId, x.StockKeepingUnitId, x.ExpirationDate
        }).HasFilter("[CompletedAtUtc] IS NULL");
        builder.HasOne<Warehouse>().WithMany().HasForeignKey(x => x.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StorageLocation>().WithMany().HasForeignKey(x => x.StorageLocationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StockKeepingUnit>().WithMany().HasForeignKey(x => x.StockKeepingUnitId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(t => t.HasCheckConstraint("CK_PickingReservations_Quantity",
            "([CompletedAtUtc] IS NULL AND [Quantity] > 0) OR ([CompletedAtUtc] IS NOT NULL AND [Quantity] = 0)"));
    }
}

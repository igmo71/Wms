using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Common;
using Wms.Domain;

namespace Wms.Data.Configurations;

internal sealed class PickingRouteReservationConfiguration
    : IEntityTypeConfiguration<PickingRouteReservation>
{
    public void Configure(EntityTypeBuilder<PickingRouteReservation> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Quantity).HasPrecision(WarehouseQuantity.Precision, WarehouseQuantity.Scale);
        builder.Property(x => x.ExpirationDate).HasColumnType("date");
        builder.HasIndex(x => new
        {
            x.WarehouseId, x.StorageLocationId, x.StockKeepingUnitId, x.ExpirationDate, x.PickingAssemblyId
        }).IsUnique()
            .HasDatabaseName(DatabaseObjectNames.PickingRouteReservationsGroupIndex)
            .HasFilter(null);
        builder.HasOne<PickingAssembly>().WithMany().HasForeignKey(x => x.PickingAssemblyId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Warehouse>().WithMany().HasForeignKey(x => x.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StorageLocation>().WithMany().HasForeignKey(x => x.StorageLocationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StockKeepingUnit>().WithMany().HasForeignKey(x => x.StockKeepingUnitId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(t => t.HasCheckConstraint("CK_PickingRouteReservations_Quantity", "[Quantity] > 0"));
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Common;
using Wms.Domain;

namespace Wms.Data.Configurations;

internal sealed class StockShortageConfiguration : IEntityTypeConfiguration<StockShortage>
{
    public void Configure(EntityTypeBuilder<StockShortage> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ExpectedQuantity).HasPrecision(WarehouseQuantity.Precision, WarehouseQuantity.Scale);
        builder.Property(x => x.FoundQuantity).HasPrecision(WarehouseQuantity.Precision, WarehouseQuantity.Scale);
        builder.Ignore(x => x.BlockedQuantity);
        builder.Property(x => x.ExpirationDate).HasColumnType("date");
        builder.Property(x => x.Comment).HasMaxLength(StockShortage.MaximumCommentLength);
        builder.Property(x => x.ResolutionReason).HasMaxLength(StockShortage.MaximumCommentLength);
        builder.Property(x => x.ReportedBy).HasMaxLength(DefaultConfiguration.Guid).IsRequired();
        builder.Property(x => x.ResolvedBy).HasMaxLength(DefaultConfiguration.Guid);
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasIndex(x => new
        {
            x.WarehouseId, x.StorageLocationId, x.StockKeepingUnitId, x.ExpirationDate
        }).IsUnique().HasFilter("[ResolvedAtUtc] IS NULL")
            .HasDatabaseName(DatabaseObjectNames.StockShortagesActiveGroupIndex);
        builder.HasIndex(x => new { x.Origin, x.OriginId });
        builder.HasOne<Warehouse>().WithMany().HasForeignKey(x => x.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StorageLocation>().WithMany().HasForeignKey(x => x.StorageLocationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StockKeepingUnit>().WithMany().HasForeignKey(x => x.StockKeepingUnitId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<InventoryMovement>().WithMany().HasForeignKey(x => x.CorrectionMovementId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(t => t.HasCheckConstraint("CK_StockShortages_Quantity",
            "[ExpectedQuantity] > [FoundQuantity] AND [FoundQuantity] >= 0"));
    }
}

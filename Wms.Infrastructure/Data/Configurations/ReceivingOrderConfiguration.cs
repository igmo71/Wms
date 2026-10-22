using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain;

namespace Wms.Data.Configurations;

internal class ReceivingOrderConfiguration : IEntityTypeConfiguration<ReceivingOrder>
{
    public void Configure(EntityTypeBuilder<ReceivingOrder> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Number).HasMaxLength(13).IsRequired();
        builder.HasIndex(x => x.Number).IsUnique();
        builder.ToTable(table => table.HasCheckConstraint(
            "CK_ReceivingOrders_NumberFormat",
            "LEN([Number]) = 13 AND [Number] LIKE '[0-9][0-9][0-9][0-9][0-9][0-9]-[0-9][0-9][0-9][0-9][0-9][0-9]'"));
        builder.Property(x => x.OperationalRevision).IsConcurrencyToken();
        builder.Property(x => x.SenderId);

        builder.Property(x => x.StartedBy).HasMaxLength(DefaultConfiguration.Guid);
        builder.Property(x => x.CompletedBy).HasMaxLength(DefaultConfiguration.Guid);

        builder.HasOne(X => X.Warehouse).WithMany()
            .HasForeignKey(X => X.WarehouseId).HasPrincipalKey(X => X.Id)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(X => X.ReceivingLocation).WithMany()
            .HasForeignKey(X => X.ReceivingLocationId).HasPrincipalKey(X => X.Id)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Navigation(x => x.Items)
            .HasField("_items")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Navigation(x => x.Participants)
            .HasField("_participants")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Navigation(x => x.LicensePlateNumbers)
            .HasField("_licensePlateNumbers")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Navigation(x => x.SourceLinks)
            .HasField("_sourceLinks")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Navigation(x => x.Cycles)
            .HasField("_cycles")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

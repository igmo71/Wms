using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Wms.Data;

#nullable disable

namespace Wms.Data.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260808180000_RefactorShippingOrderWorkflow")]
public partial class RefactorShippingOrderWorkflow : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.RenameColumn(
            name: "StartedAtUtc",
            table: "ShippingOrders",
            newName: "PickingStartedAtUtc");

        migrationBuilder.RenameColumn(
            name: "StartedBy",
            table: "ShippingOrders",
            newName: "PickingStartedBy");

        migrationBuilder.RenameColumn(
            name: "CompletedAtUtc",
            table: "ShippingOrders",
            newName: "ShippedAtUtc");

        migrationBuilder.RenameColumn(
            name: "CompletedBy",
            table: "ShippingOrders",
            newName: "ShippedBy");

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "ReadyForShipmentAtUtc",
            table: "ShippingOrders",
            type: "datetimeoffset",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ReadyForShipmentBy",
            table: "ShippingOrders",
            type: "nvarchar(36)",
            maxLength: 36,
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "ReadyForShipmentAtUtc",
            table: "ShippingOrders");

        migrationBuilder.DropColumn(
            name: "ReadyForShipmentBy",
            table: "ShippingOrders");

        migrationBuilder.RenameColumn(
            name: "PickingStartedAtUtc",
            table: "ShippingOrders",
            newName: "StartedAtUtc");

        migrationBuilder.RenameColumn(
            name: "PickingStartedBy",
            table: "ShippingOrders",
            newName: "StartedBy");

        migrationBuilder.RenameColumn(
            name: "ShippedAtUtc",
            table: "ShippingOrders",
            newName: "CompletedAtUtc");

        migrationBuilder.RenameColumn(
            name: "ShippedBy",
            table: "ShippingOrders",
            newName: "CompletedBy");
    }
}

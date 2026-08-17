using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wms.Data.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260817130000_RemoveShippingOrderItemAction")]
public partial class RemoveShippingOrderItemAction : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "Action",
            table: "ShippingOrderItems");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "Action",
            table: "ShippingOrderItems",
            type: "int",
            nullable: false,
            defaultValue: 0);
    }

    protected override void BuildTargetModel(ModelBuilder modelBuilder)
    {
    }
}

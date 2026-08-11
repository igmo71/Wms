using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wms.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddShippingOrderRollback : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RollbackReason",
                table: "ShippingOrders",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RolledBackAtUtc",
                table: "ShippingOrders",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RolledBackBy",
                table: "ShippingOrders",
                type: "nvarchar(36)",
                maxLength: 36,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RollbackReason",
                table: "ShippingOrders");

            migrationBuilder.DropColumn(
                name: "RolledBackAtUtc",
                table: "ShippingOrders");

            migrationBuilder.DropColumn(
                name: "RolledBackBy",
                table: "ShippingOrders");
        }
    }
}

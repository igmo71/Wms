using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wms.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class TrackCurrentShippingSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CurrentSourceJson",
                table: "ShippingOrders",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastSourceCheckAtUtc",
                table: "ShippingOrders",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastSourceCheckError",
                table: "ShippingOrders",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CurrentSourceJson",
                table: "ShippingOrders");

            migrationBuilder.DropColumn(
                name: "LastSourceCheckAtUtc",
                table: "ShippingOrders");

            migrationBuilder.DropColumn(
                name: "LastSourceCheckError",
                table: "ShippingOrders");
        }
    }
}

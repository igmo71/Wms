using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wms.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveOrderSynchronizationCheckedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExternalSynchronizationCheckedAtUtc",
                table: "ShippingOrders");

            migrationBuilder.DropColumn(
                name: "ExternalSynchronizationCheckedAtUtc",
                table: "ReceivingOrders");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ExternalSynchronizationCheckedAtUtc",
                table: "ShippingOrders",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ExternalSynchronizationCheckedAtUtc",
                table: "ReceivingOrders",
                type: "datetimeoffset",
                nullable: true);
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wms.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReceivingPutaway : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PutawayCompletedAtUtc",
                table: "ReceivingOrders",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PutawayCompletedBy",
                table: "ReceivingOrders",
                type: "nvarchar(36)",
                maxLength: 36,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PutawayStartedAtUtc",
                table: "ReceivingOrders",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PutawayStartedBy",
                table: "ReceivingOrders",
                type: "nvarchar(36)",
                maxLength: 36,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PutawayStatus",
                table: "ReceivingOrders",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PutawayCompletedAtUtc",
                table: "ReceivingOrders");

            migrationBuilder.DropColumn(
                name: "PutawayCompletedBy",
                table: "ReceivingOrders");

            migrationBuilder.DropColumn(
                name: "PutawayStartedAtUtc",
                table: "ReceivingOrders");

            migrationBuilder.DropColumn(
                name: "PutawayStartedBy",
                table: "ReceivingOrders");

            migrationBuilder.DropColumn(
                name: "PutawayStatus",
                table: "ReceivingOrders");
        }
    }
}

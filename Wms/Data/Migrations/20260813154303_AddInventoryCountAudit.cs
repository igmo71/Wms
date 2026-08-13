using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wms.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryCountAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InventoryCounts_WarehouseId",
                table: "InventoryCounts");

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "InventoryCounts",
                type: "nvarchar(36)",
                maxLength: 36,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "Date",
                table: "InventoryCounts",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "Number",
                table: "InventoryCounts",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PostedBy",
                table: "InventoryCounts",
                type: "nvarchar(36)",
                maxLength: 36,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UpdatedBy",
                table: "InventoryCounts",
                type: "nvarchar(36)",
                maxLength: 36,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAtUtc",
                table: "InventoryCountItems",
                type: "datetimeoffset",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "InventoryCountItems",
                type: "nvarchar(36)",
                maxLength: 36,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "UpdatedBy",
                table: "InventoryCountItems",
                type: "nvarchar(36)",
                maxLength: 36,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryCounts_Number",
                table: "InventoryCounts",
                column: "Number");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryCounts_WarehouseId_Date",
                table: "InventoryCounts",
                columns: new[] { "WarehouseId", "Date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InventoryCounts_Number",
                table: "InventoryCounts");

            migrationBuilder.DropIndex(
                name: "IX_InventoryCounts_WarehouseId_Date",
                table: "InventoryCounts");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "InventoryCounts");

            migrationBuilder.DropColumn(
                name: "Date",
                table: "InventoryCounts");

            migrationBuilder.DropColumn(
                name: "Number",
                table: "InventoryCounts");

            migrationBuilder.DropColumn(
                name: "PostedBy",
                table: "InventoryCounts");

            migrationBuilder.DropColumn(
                name: "UpdatedBy",
                table: "InventoryCounts");

            migrationBuilder.DropColumn(
                name: "CreatedAtUtc",
                table: "InventoryCountItems");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "InventoryCountItems");

            migrationBuilder.DropColumn(
                name: "UpdatedBy",
                table: "InventoryCountItems");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryCounts_WarehouseId",
                table: "InventoryCounts",
                column: "WarehouseId");
        }
    }
}

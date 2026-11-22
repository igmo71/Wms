using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wms.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class DatedInventoryCount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_InventoryCountItems_InventoryCountId_StockKeepingUnitId",
                table: "InventoryCountItems");

            migrationBuilder.AddColumn<Guid>(
                name: "StorageLocationId",
                table: "WarehouseAuditEvents",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ExpirationConfirmed",
                table: "InventoryCountItems",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ExpirationDate",
                table: "InventoryCountItems",
                type: "date",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WarehouseAuditEvents_StorageLocationId_OccurredAtUtc",
                table: "WarehouseAuditEvents",
                columns: new[] { "StorageLocationId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_InventoryCountItems_CountId_SkuId_ExpirationDate",
                table: "InventoryCountItems",
                columns: new[] { "InventoryCountId", "StockKeepingUnitId", "ExpirationDate" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_WarehouseAuditEvents_StorageLocations_StorageLocationId",
                table: "WarehouseAuditEvents",
                column: "StorageLocationId",
                principalTable: "StorageLocations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WarehouseAuditEvents_StorageLocations_StorageLocationId",
                table: "WarehouseAuditEvents");

            migrationBuilder.DropIndex(
                name: "IX_WarehouseAuditEvents_StorageLocationId_OccurredAtUtc",
                table: "WarehouseAuditEvents");

            migrationBuilder.DropIndex(
                name: "UX_InventoryCountItems_CountId_SkuId_ExpirationDate",
                table: "InventoryCountItems");

            migrationBuilder.DropColumn(
                name: "StorageLocationId",
                table: "WarehouseAuditEvents");

            migrationBuilder.DropColumn(
                name: "ExpirationConfirmed",
                table: "InventoryCountItems");

            migrationBuilder.DropColumn(
                name: "ExpirationDate",
                table: "InventoryCountItems");

            migrationBuilder.CreateIndex(
                name: "UX_InventoryCountItems_InventoryCountId_StockKeepingUnitId",
                table: "InventoryCountItems",
                columns: new[] { "InventoryCountId", "StockKeepingUnitId" },
                unique: true);
        }
    }
}

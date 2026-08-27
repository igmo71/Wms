using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wms.Data.Migrations
{
    /// <inheritdoc />
    public partial class RebuildLocationInventoryCounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DELETE FROM [StorageLocationLocks] WHERE [OwnerType] = 1; "
                + "DELETE FROM [InventoryCountItems]; "
                + "DELETE FROM [InventoryCounts];");

            migrationBuilder.DropForeignKey(
                name: "FK_InventoryCountItems_StorageLocations_StorageLocationId",
                table: "InventoryCountItems");

            migrationBuilder.DropIndex(
                name: "IX_InventoryCountItems_StorageLocationId",
                table: "InventoryCountItems");

            migrationBuilder.DropColumn(
                name: "StorageLocationId",
                table: "InventoryCountItems");

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "InventoryCounts",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<Guid>(
                name: "StorageLocationId",
                table: "InventoryCounts",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AlterColumn<Guid>(
                name: "StockKeepingUnitId",
                table: "InventoryCountItems",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<double>(
                name: "CountedQuantity",
                table: "InventoryCountItems",
                type: "float",
                nullable: true,
                oldClrType: typeof(double),
                oldType: "float");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryCounts_StorageLocationId",
                table: "InventoryCounts",
                column: "StorageLocationId");

            migrationBuilder.CreateIndex(
                name: "UX_InventoryCountItems_InventoryCountId_StockKeepingUnitId",
                table: "InventoryCountItems",
                columns: new[] { "InventoryCountId", "StockKeepingUnitId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryCounts_StorageLocations_StorageLocationId",
                table: "InventoryCounts",
                column: "StorageLocationId",
                principalTable: "StorageLocations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_InventoryCounts_StorageLocations_StorageLocationId",
                table: "InventoryCounts");

            migrationBuilder.DropIndex(
                name: "IX_InventoryCounts_StorageLocationId",
                table: "InventoryCounts");

            migrationBuilder.DropIndex(
                name: "UX_InventoryCountItems_InventoryCountId_StockKeepingUnitId",
                table: "InventoryCountItems");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "InventoryCounts");

            migrationBuilder.DropColumn(
                name: "StorageLocationId",
                table: "InventoryCounts");

            migrationBuilder.AlterColumn<Guid>(
                name: "StockKeepingUnitId",
                table: "InventoryCountItems",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<double>(
                name: "CountedQuantity",
                table: "InventoryCountItems",
                type: "float",
                nullable: false,
                defaultValue: 0.0,
                oldClrType: typeof(double),
                oldType: "float",
                oldNullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "StorageLocationId",
                table: "InventoryCountItems",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryCountItems_StorageLocationId",
                table: "InventoryCountItems",
                column: "StorageLocationId");

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryCountItems_StorageLocations_StorageLocationId",
                table: "InventoryCountItems",
                column: "StorageLocationId",
                principalTable: "StorageLocations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}

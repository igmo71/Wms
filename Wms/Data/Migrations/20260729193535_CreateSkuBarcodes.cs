using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wms.Data.Migrations
{
    /// <inheritdoc />
    public partial class CreateSkuBarcodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StorageLocation_Warehouses_WarehouseId",
                table: "StorageLocation");

            migrationBuilder.DropForeignKey(
                name: "FK_StorageLocation_Zone_ZoneId",
                table: "StorageLocation");

            migrationBuilder.DropForeignKey(
                name: "FK_Zone_Warehouses_WarehouseId",
                table: "Zone");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Zone",
                table: "Zone");

            migrationBuilder.DropPrimaryKey(
                name: "PK_StorageLocation",
                table: "StorageLocation");

            migrationBuilder.RenameTable(
                name: "Zone",
                newName: "Zones");

            migrationBuilder.RenameTable(
                name: "StorageLocation",
                newName: "StorageLocations");

            migrationBuilder.RenameIndex(
                name: "IX_Zone_WarehouseId",
                table: "Zones",
                newName: "IX_Zones_WarehouseId");

            migrationBuilder.RenameIndex(
                name: "IX_StorageLocation_ZoneId",
                table: "StorageLocations",
                newName: "IX_StorageLocations_ZoneId");

            migrationBuilder.RenameIndex(
                name: "IX_StorageLocation_WarehouseId",
                table: "StorageLocations",
                newName: "IX_StorageLocations_WarehouseId");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Zones",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "StorageLocations",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_Zones",
                table: "Zones",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_StorageLocations",
                table: "StorageLocations",
                column: "Id");

            migrationBuilder.CreateTable(
                name: "SkuBarcodes",
                columns: table => new
                {
                    SkuId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Value = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SkuBarcodes", x => new { x.SkuId, x.Value });
                    table.ForeignKey(
                        name: "FK_SkuBarcodes_StockKeepingUnits_SkuId",
                        column: x => x.SkuId,
                        principalTable: "StockKeepingUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddForeignKey(
                name: "FK_StorageLocations_Warehouses_WarehouseId",
                table: "StorageLocations",
                column: "WarehouseId",
                principalTable: "Warehouses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_StorageLocations_Zones_ZoneId",
                table: "StorageLocations",
                column: "ZoneId",
                principalTable: "Zones",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Zones_Warehouses_WarehouseId",
                table: "Zones",
                column: "WarehouseId",
                principalTable: "Warehouses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StorageLocations_Warehouses_WarehouseId",
                table: "StorageLocations");

            migrationBuilder.DropForeignKey(
                name: "FK_StorageLocations_Zones_ZoneId",
                table: "StorageLocations");

            migrationBuilder.DropForeignKey(
                name: "FK_Zones_Warehouses_WarehouseId",
                table: "Zones");

            migrationBuilder.DropTable(
                name: "SkuBarcodes");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Zones",
                table: "Zones");

            migrationBuilder.DropPrimaryKey(
                name: "PK_StorageLocations",
                table: "StorageLocations");

            migrationBuilder.RenameTable(
                name: "Zones",
                newName: "Zone");

            migrationBuilder.RenameTable(
                name: "StorageLocations",
                newName: "StorageLocation");

            migrationBuilder.RenameIndex(
                name: "IX_Zones_WarehouseId",
                table: "Zone",
                newName: "IX_Zone_WarehouseId");

            migrationBuilder.RenameIndex(
                name: "IX_StorageLocations_ZoneId",
                table: "StorageLocation",
                newName: "IX_StorageLocation_ZoneId");

            migrationBuilder.RenameIndex(
                name: "IX_StorageLocations_WarehouseId",
                table: "StorageLocation",
                newName: "IX_StorageLocation_WarehouseId");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Zone",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "StorageLocation",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_Zone",
                table: "Zone",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_StorageLocation",
                table: "StorageLocation",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_StorageLocation_Warehouses_WarehouseId",
                table: "StorageLocation",
                column: "WarehouseId",
                principalTable: "Warehouses",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_StorageLocation_Zone_ZoneId",
                table: "StorageLocation",
                column: "ZoneId",
                principalTable: "Zone",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Zone_Warehouses_WarehouseId",
                table: "Zone",
                column: "WarehouseId",
                principalTable: "Warehouses",
                principalColumn: "Id");
        }
    }
}

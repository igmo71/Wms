using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wms.Data.Migrations
{
    /// <inheritdoc />
    public partial class UseWarehouseStorageAddresses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StorageLocations_WarehouseId",
                table: "StorageLocations");

            migrationBuilder.DropIndex(
                name: "IX_StorageLocations_ZoneId_Code",
                table: "StorageLocations");

            migrationBuilder.DropColumn(
                name: "Number",
                table: "StorageLocations");

            migrationBuilder.AlterColumn<Guid>(
                name: "ZoneId",
                table: "StorageLocations",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.CreateIndex(
                name: "IX_StorageLocations_WarehouseId_Code",
                table: "StorageLocations",
                columns: new[] { "WarehouseId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StorageLocations_ZoneId",
                table: "StorageLocations",
                column: "ZoneId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_StorageLocations_OperationalZone",
                table: "StorageLocations",
                sql: "[IsFolder] = 1 OR [ZoneId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StorageLocations_WarehouseId_Code",
                table: "StorageLocations");

            migrationBuilder.DropIndex(
                name: "IX_StorageLocations_ZoneId",
                table: "StorageLocations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_StorageLocations_OperationalZone",
                table: "StorageLocations");

            migrationBuilder.AlterColumn<Guid>(
                name: "ZoneId",
                table: "StorageLocations",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Number",
                table: "StorageLocations",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_StorageLocations_WarehouseId",
                table: "StorageLocations",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_StorageLocations_ZoneId_Code",
                table: "StorageLocations",
                columns: new[] { "ZoneId", "Code" },
                unique: true);
        }
    }
}

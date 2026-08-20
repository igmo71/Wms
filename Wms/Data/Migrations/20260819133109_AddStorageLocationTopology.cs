using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wms.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStorageLocationTopology : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Zones_WarehouseId",
                table: "Zones");

            migrationBuilder.DropIndex(
                name: "IX_StorageLocations_ZoneId",
                table: "StorageLocations");

            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "Zones",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "StorageLocations",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<double>(
                name: "Coordinates_X",
                table: "StorageLocations",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Coordinates_Y",
                table: "StorageLocations",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Coordinates_Z",
                table: "StorageLocations",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Dimensions_Height",
                table: "StorageLocations",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Dimensions_Length",
                table: "StorageLocations",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Dimensions_MaxWeight",
                table: "StorageLocations",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Dimensions_Volume",
                table: "StorageLocations",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Dimensions_VolumeFactor",
                table: "StorageLocations",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Dimensions_Width",
                table: "StorageLocations",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsFolder",
                table: "StorageLocations",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "Number",
                table: "StorageLocations",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "ParentId",
                table: "StorageLocations",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "PickSequence",
                table: "StorageLocations",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Zones_WarehouseId_Code",
                table: "Zones",
                columns: new[] { "WarehouseId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StorageLocations_ParentId",
                table: "StorageLocations",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_StorageLocations_ZoneId_Code",
                table: "StorageLocations",
                columns: new[] { "ZoneId", "Code" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_StorageLocations_StorageLocations_ParentId",
                table: "StorageLocations",
                column: "ParentId",
                principalTable: "StorageLocations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StorageLocations_StorageLocations_ParentId",
                table: "StorageLocations");

            migrationBuilder.DropIndex(
                name: "IX_Zones_WarehouseId_Code",
                table: "Zones");

            migrationBuilder.DropIndex(
                name: "IX_StorageLocations_ParentId",
                table: "StorageLocations");

            migrationBuilder.DropIndex(
                name: "IX_StorageLocations_ZoneId_Code",
                table: "StorageLocations");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "Zones");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "StorageLocations");

            migrationBuilder.DropColumn(
                name: "Coordinates_X",
                table: "StorageLocations");

            migrationBuilder.DropColumn(
                name: "Coordinates_Y",
                table: "StorageLocations");

            migrationBuilder.DropColumn(
                name: "Coordinates_Z",
                table: "StorageLocations");

            migrationBuilder.DropColumn(
                name: "Dimensions_Height",
                table: "StorageLocations");

            migrationBuilder.DropColumn(
                name: "Dimensions_Length",
                table: "StorageLocations");

            migrationBuilder.DropColumn(
                name: "Dimensions_MaxWeight",
                table: "StorageLocations");

            migrationBuilder.DropColumn(
                name: "Dimensions_Volume",
                table: "StorageLocations");

            migrationBuilder.DropColumn(
                name: "Dimensions_VolumeFactor",
                table: "StorageLocations");

            migrationBuilder.DropColumn(
                name: "Dimensions_Width",
                table: "StorageLocations");

            migrationBuilder.DropColumn(
                name: "IsFolder",
                table: "StorageLocations");

            migrationBuilder.DropColumn(
                name: "Number",
                table: "StorageLocations");

            migrationBuilder.DropColumn(
                name: "ParentId",
                table: "StorageLocations");

            migrationBuilder.DropColumn(
                name: "PickSequence",
                table: "StorageLocations");

            migrationBuilder.CreateIndex(
                name: "IX_Zones_WarehouseId",
                table: "Zones",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_StorageLocations_ZoneId",
                table: "StorageLocations",
                column: "ZoneId");
        }
    }
}

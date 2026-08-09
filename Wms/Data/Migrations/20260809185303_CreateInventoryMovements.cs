using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wms.Data.Migrations
{
    /// <inheritdoc />
    public partial class CreateInventoryMovements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InventoryTurnovers_RecorderType_RecorderId_RecorderLineNumber",
                table: "InventoryTurnovers");

            migrationBuilder.AlterColumn<int>(
                name: "RecorderLineNumber",
                table: "InventoryTurnovers",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.CreateTable(
                name: "InventoryMovements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceStorageLocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DestinationStorageLocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    StockKeepingUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quantity = table.Column<double>(type: "float", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PostedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RecorderId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RecorderLineNumber = table.Column<int>(type: "int", nullable: true),
                    RecorderType = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryMovements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InventoryMovements_StockKeepingUnits_StockKeepingUnitId",
                        column: x => x.StockKeepingUnitId,
                        principalTable: "StockKeepingUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryMovements_StorageLocations_DestinationStorageLocationId",
                        column: x => x.DestinationStorageLocationId,
                        principalTable: "StorageLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryMovements_StorageLocations_SourceStorageLocationId",
                        column: x => x.SourceStorageLocationId,
                        principalTable: "StorageLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryMovements_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTurnovers_RecorderType_RecorderId_RecorderLineNumber",
                table: "InventoryTurnovers",
                columns: new[] { "RecorderType", "RecorderId", "RecorderLineNumber" },
                unique: true,
                filter: "[RecorderId] IS NOT NULL AND [RecorderLineNumber] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovements_DestinationStorageLocationId",
                table: "InventoryMovements",
                column: "DestinationStorageLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovements_SourceStorageLocationId",
                table: "InventoryMovements",
                column: "SourceStorageLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovements_StockKeepingUnitId",
                table: "InventoryMovements",
                column: "StockKeepingUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovements_WarehouseId",
                table: "InventoryMovements",
                column: "WarehouseId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InventoryMovements");

            migrationBuilder.DropIndex(
                name: "IX_InventoryTurnovers_RecorderType_RecorderId_RecorderLineNumber",
                table: "InventoryTurnovers");

            migrationBuilder.AlterColumn<int>(
                name: "RecorderLineNumber",
                table: "InventoryTurnovers",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTurnovers_RecorderType_RecorderId_RecorderLineNumber",
                table: "InventoryTurnovers",
                columns: new[] { "RecorderType", "RecorderId", "RecorderLineNumber" },
                unique: true,
                filter: "[RecorderId] IS NOT NULL");
        }
    }
}

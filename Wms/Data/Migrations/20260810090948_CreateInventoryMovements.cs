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

            migrationBuilder.DropColumn(
                name: "RecorderId",
                table: "InventoryTurnovers");

            migrationBuilder.DropColumn(
                name: "RecorderLineNumber",
                table: "InventoryTurnovers");

            migrationBuilder.DropColumn(
                name: "RecorderType",
                table: "InventoryTurnovers");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "CreatedAtUtc",
                table: "ShippingOrders",
                type: "datetimeoffset",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)),
                oldClrType: typeof(DateTimeOffset),
                oldType: "datetimeoffset",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "CreatedAtUtc",
                table: "ReceivingOrders",
                type: "datetimeoffset",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)),
                oldClrType: typeof(DateTimeOffset),
                oldType: "datetimeoffset",
                oldNullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "InventoryMovementId",
                table: "InventoryTurnovers",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "UpdatedAtUtc",
                table: "InventoryBalances",
                type: "datetimeoffset",
                nullable: true,
                oldClrType: typeof(DateTimeOffset),
                oldType: "datetimeoffset");

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
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
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
                name: "IX_InventoryTurnovers_InventoryMovementId",
                table: "InventoryTurnovers",
                column: "InventoryMovementId");

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

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryTurnovers_InventoryMovements_InventoryMovementId",
                table: "InventoryTurnovers",
                column: "InventoryMovementId",
                principalTable: "InventoryMovements",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_InventoryTurnovers_InventoryMovements_InventoryMovementId",
                table: "InventoryTurnovers");

            migrationBuilder.DropTable(
                name: "InventoryMovements");

            migrationBuilder.DropIndex(
                name: "IX_InventoryTurnovers_InventoryMovementId",
                table: "InventoryTurnovers");

            migrationBuilder.DropColumn(
                name: "InventoryMovementId",
                table: "InventoryTurnovers");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "CreatedAtUtc",
                table: "ShippingOrders",
                type: "datetimeoffset",
                nullable: true,
                oldClrType: typeof(DateTimeOffset),
                oldType: "datetimeoffset");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "CreatedAtUtc",
                table: "ReceivingOrders",
                type: "datetimeoffset",
                nullable: true,
                oldClrType: typeof(DateTimeOffset),
                oldType: "datetimeoffset");

            migrationBuilder.AddColumn<Guid>(
                name: "RecorderId",
                table: "InventoryTurnovers",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RecorderLineNumber",
                table: "InventoryTurnovers",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RecorderType",
                table: "InventoryTurnovers",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "UpdatedAtUtc",
                table: "InventoryBalances",
                type: "datetimeoffset",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)),
                oldClrType: typeof(DateTimeOffset),
                oldType: "datetimeoffset",
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

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Wms.Data;

#nullable disable

namespace Wms.Data.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260809140000_CreateInventoryMovements")]
public partial class CreateInventoryMovements : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
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

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "InventoryMovements");

        migrationBuilder.AlterColumn<int>(
            name: "RecorderLineNumber",
            table: "InventoryTurnovers",
            type: "int",
            nullable: false,
            defaultValue: 0,
            oldClrType: typeof(int),
            oldType: "int",
            oldNullable: true);
    }
}

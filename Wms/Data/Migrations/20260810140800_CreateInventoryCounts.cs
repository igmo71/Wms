using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wms.Data.Migrations
{
    /// <inheritdoc />
    public partial class CreateInventoryCounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<DateTime>(
                name: "PlannedShippingDate",
                table: "ShippingOrders",
                type: "datetime2",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "datetime2");

            migrationBuilder.CreateTable(
                name: "InventoryCounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PostedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryCounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InventoryCounts_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InventoryCountItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InventoryCountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineNumber = table.Column<int>(type: "int", nullable: false),
                    StorageLocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    StockKeepingUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ExpectedQuantity = table.Column<double>(type: "float", nullable: false),
                    CountedQuantity = table.Column<double>(type: "float", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryCountItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InventoryCountItems_InventoryCounts_InventoryCountId",
                        column: x => x.InventoryCountId,
                        principalTable: "InventoryCounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_InventoryCountItems_StockKeepingUnits_StockKeepingUnitId",
                        column: x => x.StockKeepingUnitId,
                        principalTable: "StockKeepingUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryCountItems_StorageLocations_StorageLocationId",
                        column: x => x.StorageLocationId,
                        principalTable: "StorageLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryCountItems_InventoryCountId_LineNumber",
                table: "InventoryCountItems",
                columns: new[] { "InventoryCountId", "LineNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryCountItems_StockKeepingUnitId",
                table: "InventoryCountItems",
                column: "StockKeepingUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryCountItems_StorageLocationId",
                table: "InventoryCountItems",
                column: "StorageLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryCounts_WarehouseId",
                table: "InventoryCounts",
                column: "WarehouseId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InventoryCountItems");

            migrationBuilder.DropTable(
                name: "InventoryCounts");

            migrationBuilder.AlterColumn<DateTime>(
                name: "PlannedShippingDate",
                table: "ShippingOrders",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified),
                oldClrType: typeof(DateTime),
                oldType: "datetime2",
                oldNullable: true);
        }
    }
}

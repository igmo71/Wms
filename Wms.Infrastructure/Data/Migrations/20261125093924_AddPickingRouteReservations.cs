using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wms.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPickingRouteReservations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PickingRouteReservations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PickingAssemblyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StorageLocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StockKeepingUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExpirationDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Quantity = table.Column<decimal>(type: "decimal(15,3)", precision: 15, scale: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PickingRouteReservations", x => x.Id);
                    table.CheckConstraint("CK_PickingRouteReservations_Quantity", "[Quantity] > 0");
                    table.ForeignKey(
                        name: "FK_PickingRouteReservations_PickingAssemblies_PickingAssemblyId",
                        column: x => x.PickingAssemblyId,
                        principalTable: "PickingAssemblies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PickingRouteReservations_StockKeepingUnits_StockKeepingUnitId",
                        column: x => x.StockKeepingUnitId,
                        principalTable: "StockKeepingUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PickingRouteReservations_StorageLocations_StorageLocationId",
                        column: x => x.StorageLocationId,
                        principalTable: "StorageLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PickingRouteReservations_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PickingRouteReservations_PickingAssemblyId",
                table: "PickingRouteReservations",
                column: "PickingAssemblyId");

            migrationBuilder.CreateIndex(
                name: "IX_PickingRouteReservations_StockKeepingUnitId",
                table: "PickingRouteReservations",
                column: "StockKeepingUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_PickingRouteReservations_StorageLocationId",
                table: "PickingRouteReservations",
                column: "StorageLocationId");

            migrationBuilder.CreateIndex(
                name: "UX_PickingRouteReservations_Group",
                table: "PickingRouteReservations",
                columns: new[] { "WarehouseId", "StorageLocationId", "StockKeepingUnitId", "ExpirationDate", "PickingAssemblyId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PickingRouteReservations");
        }
    }
}

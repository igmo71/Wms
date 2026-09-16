using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wms.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLicensePlateNumberPutaway : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LicensePlateNumberPutaways",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LicensePlateNumberId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LicensePlateNumberItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StockKeepingUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(15,3)", precision: 15, scale: 3, nullable: false),
                    DestinationStorageLocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InventoryMovementId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LicensePlateNumberPutaways", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LicensePlateNumberPutaways_InventoryMovements_InventoryMovementId",
                        column: x => x.InventoryMovementId,
                        principalTable: "InventoryMovements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LicensePlateNumberPutaways_LicensePlateNumberItems_LicensePlateNumberItemId",
                        column: x => x.LicensePlateNumberItemId,
                        principalTable: "LicensePlateNumberItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LicensePlateNumberPutaways_LicensePlateNumbers_LicensePlateNumberId",
                        column: x => x.LicensePlateNumberId,
                        principalTable: "LicensePlateNumbers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LicensePlateNumberPutaways_StockKeepingUnits_StockKeepingUnitId",
                        column: x => x.StockKeepingUnitId,
                        principalTable: "StockKeepingUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LicensePlateNumberPutaways_StorageLocations_DestinationStorageLocationId",
                        column: x => x.DestinationStorageLocationId,
                        principalTable: "StorageLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LicensePlateNumberPutaways_DestinationStorageLocationId",
                table: "LicensePlateNumberPutaways",
                column: "DestinationStorageLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_LicensePlateNumberPutaways_InventoryMovementId",
                table: "LicensePlateNumberPutaways",
                column: "InventoryMovementId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LicensePlateNumberPutaways_LicensePlateNumberId",
                table: "LicensePlateNumberPutaways",
                column: "LicensePlateNumberId");

            migrationBuilder.CreateIndex(
                name: "IX_LicensePlateNumberPutaways_LicensePlateNumberItemId",
                table: "LicensePlateNumberPutaways",
                column: "LicensePlateNumberItemId");

            migrationBuilder.CreateIndex(
                name: "IX_LicensePlateNumberPutaways_StockKeepingUnitId",
                table: "LicensePlateNumberPutaways",
                column: "StockKeepingUnitId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LicensePlateNumberPutaways");
        }
    }
}

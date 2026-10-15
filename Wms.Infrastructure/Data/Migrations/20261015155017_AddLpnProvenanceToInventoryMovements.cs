using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wms.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLpnProvenanceToInventoryMovements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "LicensePlateNumberId",
                table: "InventoryMovements",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovements_LicensePlateNumberId",
                table: "InventoryMovements",
                column: "LicensePlateNumberId");

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryMovements_LicensePlateNumbers_LicensePlateNumberId",
                table: "InventoryMovements",
                column: "LicensePlateNumberId",
                principalTable: "LicensePlateNumbers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_InventoryMovements_LicensePlateNumbers_LicensePlateNumberId",
                table: "InventoryMovements");

            migrationBuilder.DropIndex(
                name: "IX_InventoryMovements_LicensePlateNumberId",
                table: "InventoryMovements");

            migrationBuilder.DropColumn(
                name: "LicensePlateNumberId",
                table: "InventoryMovements");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wms.Data.Migrations
{
    /// <inheritdoc />
    public partial class RefactorInventoryTurnoverToInventoryMovement : Migration
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

            migrationBuilder.AddColumn<Guid>(
                name: "InventoryMovementId",
                table: "InventoryTurnovers",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTurnovers_InventoryMovementId",
                table: "InventoryTurnovers",
                column: "InventoryMovementId");

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

            migrationBuilder.DropIndex(
                name: "IX_InventoryTurnovers_InventoryMovementId",
                table: "InventoryTurnovers");

            migrationBuilder.DropColumn(
                name: "InventoryMovementId",
                table: "InventoryTurnovers");

            migrationBuilder.AddColumn<Guid>(
                name: "RecorderId",
                table: "InventoryTurnovers",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RecorderLineNumber",
                table: "InventoryTurnovers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RecorderType",
                table: "InventoryTurnovers",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTurnovers_RecorderType_RecorderId_RecorderLineNumber",
                table: "InventoryTurnovers",
                columns: new[] { "RecorderType", "RecorderId", "RecorderLineNumber" },
                unique: true,
                filter: "[RecorderId] IS NOT NULL AND [RecorderLineNumber] IS NOT NULL");
        }
    }
}

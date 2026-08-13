using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wms.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTransferCommandFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TransferOrders_Number",
                table: "TransferOrders");

            migrationBuilder.AddColumn<long>(
                name: "SequenceNumber",
                table: "TransferOrders",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "ConfirmedBy",
                table: "InventoryMovements",
                type: "nvarchar(36)",
                maxLength: 36,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TransferOrders_Number",
                table: "TransferOrders",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TransferOrders_SequenceNumber",
                table: "TransferOrders",
                column: "SequenceNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovements_RecorderType_RecorderId_RecorderLineNumber",
                table: "InventoryMovements",
                columns: new[] { "RecorderType", "RecorderId", "RecorderLineNumber" },
                unique: true,
                filter: "[RecorderType] = 4 AND [RecorderId] IS NOT NULL AND [RecorderLineNumber] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TransferOrders_Number",
                table: "TransferOrders");

            migrationBuilder.DropIndex(
                name: "IX_TransferOrders_SequenceNumber",
                table: "TransferOrders");

            migrationBuilder.DropIndex(
                name: "IX_InventoryMovements_RecorderType_RecorderId_RecorderLineNumber",
                table: "InventoryMovements");

            migrationBuilder.DropColumn(
                name: "SequenceNumber",
                table: "TransferOrders");

            migrationBuilder.DropColumn(
                name: "ConfirmedBy",
                table: "InventoryMovements");

            migrationBuilder.CreateIndex(
                name: "IX_TransferOrders_Number",
                table: "TransferOrders",
                column: "Number");
        }
    }
}

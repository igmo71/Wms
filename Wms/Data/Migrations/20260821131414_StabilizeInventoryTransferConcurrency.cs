using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wms.Data.Migrations
{
    /// <inheritdoc />
    public partial class StabilizeInventoryTransferConcurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameIndex(
                name: "IX_InventoryBalances_WarehouseId_StorageLocationId_StockKeepingUnitId",
                table: "InventoryBalances",
                newName: "UX_InventoryBalances_BusinessKey");

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "InventoryTransfers",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.CreateIndex(
                name: "UX_InventoryMovements_TransferLine",
                table: "InventoryMovements",
                columns: new[] { "RecorderType", "RecorderId", "RecorderLineNumber" },
                unique: true,
                filter: "[RecorderType] = 4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_InventoryMovements_TransferLine",
                table: "InventoryMovements");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "InventoryTransfers");

            migrationBuilder.RenameIndex(
                name: "UX_InventoryBalances_BusinessKey",
                table: "InventoryBalances",
                newName: "IX_InventoryBalances_WarehouseId_StorageLocationId_StockKeepingUnitId");
        }
    }
}

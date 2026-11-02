using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wms.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOpeningBalanceRecorderLine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "UX_InventoryMovements_OpeningBalanceLine",
                table: "InventoryMovements",
                columns: new[] { "RecorderId", "RecorderLineNumber" },
                unique: true,
                filter: "[RecorderType] = 5");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_InventoryMovements_OpeningBalanceLine",
                table: "InventoryMovements");
        }
    }
}

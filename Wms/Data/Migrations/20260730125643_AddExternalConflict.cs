using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wms.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddExternalConflict : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "PlannQuantity",
                table: "ReceivingOrderItems",
                newName: "PlanQuantity");

            migrationBuilder.AddColumn<string>(
                name: "ExternalConflict",
                table: "ReceivingOrders",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExternalConflict",
                table: "ReceivingOrders");

            migrationBuilder.RenameColumn(
                name: "PlanQuantity",
                table: "ReceivingOrderItems",
                newName: "PlannQuantity");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wms.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddComment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExternalConflict",
                table: "ReceivingOrders");

            migrationBuilder.AddColumn<string>(
                name: "Comment",
                table: "ReceivingOrderItems",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Comment",
                table: "ReceivingOrderItems");

            migrationBuilder.AddColumn<string>(
                name: "ExternalConflict",
                table: "ReceivingOrders",
                type: "nvarchar(max)",
                nullable: true);
        }
    }
}

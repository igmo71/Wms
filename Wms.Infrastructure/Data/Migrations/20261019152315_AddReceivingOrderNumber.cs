using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wms.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReceivingOrderNumber : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Number",
                table: "ReceivingOrders",
                type: "nvarchar(13)",
                maxLength: 13,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_ReceivingOrders_Number",
                table: "ReceivingOrders",
                column: "Number",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_ReceivingOrders_NumberFormat",
                table: "ReceivingOrders",
                sql: "LEN([Number]) = 13 AND [Number] LIKE '[0-9][0-9][0-9][0-9][0-9][0-9]-[0-9][0-9][0-9][0-9][0-9][0-9]'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ReceivingOrders_Number",
                table: "ReceivingOrders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ReceivingOrders_NumberFormat",
                table: "ReceivingOrders");

            migrationBuilder.DropColumn(
                name: "Number",
                table: "ReceivingOrders");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wms.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReceivingManagerCompletion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CompletionDecisionJson",
                table: "ReceivingOrders",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "IntegrationMode",
                table: "ReceivingOrders",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "SourceSnapshotJson",
                table: "ReceivingOrders",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CompletionDecisionJson",
                table: "ReceivingOrders");

            migrationBuilder.DropColumn(
                name: "IntegrationMode",
                table: "ReceivingOrders");

            migrationBuilder.DropColumn(
                name: "SourceSnapshotJson",
                table: "ReceivingOrders");
        }
    }
}

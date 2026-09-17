using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wms.Data.Migrations
{
    /// <inheritdoc />
    public partial class AllowMultipleOpenReceivingLpns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_LicensePlateNumbers_OpenOwner",
                table: "LicensePlateNumbers");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "UX_LicensePlateNumbers_OpenOwner",
                table: "LicensePlateNumbers",
                columns: new[] { "ReceivingOrderId", "ResponsibleUserId" },
                unique: true,
                filter: "[Status] = 1");
        }
    }
}

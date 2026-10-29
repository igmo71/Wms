using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wms.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStorageLocationSkuMixing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AllowsMultipleSkus",
                table: "StorageLocations",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AllowsMultipleSkus",
                table: "StorageLocations");
        }
    }
}

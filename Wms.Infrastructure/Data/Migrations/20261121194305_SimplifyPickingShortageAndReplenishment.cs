using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wms.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class SimplifyPickingShortageAndReplenishment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StockShortages_Origin_OriginId",
                table: "StockShortages");

            migrationBuilder.DropColumn(
                name: "Origin",
                table: "StockShortages");

            migrationBuilder.DropColumn(
                name: "SourceWasEmpty",
                table: "ReplenishmentSourceCycles");

            migrationBuilder.RenameColumn(
                name: "OriginId",
                table: "StockShortages",
                newName: "PickingAssemblyId");

            migrationBuilder.CreateIndex(
                name: "IX_StockShortages_PickingAssemblyId",
                table: "StockShortages",
                column: "PickingAssemblyId");

            migrationBuilder.AddForeignKey(
                name: "FK_StockShortages_PickingAssemblies_PickingAssemblyId",
                table: "StockShortages",
                column: "PickingAssemblyId",
                principalTable: "PickingAssemblies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StockShortages_PickingAssemblies_PickingAssemblyId",
                table: "StockShortages");

            migrationBuilder.DropIndex(
                name: "IX_StockShortages_PickingAssemblyId",
                table: "StockShortages");

            migrationBuilder.RenameColumn(
                name: "PickingAssemblyId",
                table: "StockShortages",
                newName: "OriginId");

            migrationBuilder.AddColumn<int>(
                name: "Origin",
                table: "StockShortages",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<bool>(
                name: "SourceWasEmpty",
                table: "ReplenishmentSourceCycles",
                type: "bit",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockShortages_Origin_OriginId",
                table: "StockShortages",
                columns: new[] { "Origin", "OriginId" });
        }
    }
}

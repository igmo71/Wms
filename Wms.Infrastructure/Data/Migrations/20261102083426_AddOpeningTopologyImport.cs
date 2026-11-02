using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wms.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOpeningTopologyImport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "WarehouseOpeningImportId",
                table: "WarehouseAuditEvents",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "WarehouseOpeningImports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    SourceSha256 = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    DeclaredSnapshotAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    TopologyImportedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    TopologyImportedBy = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: false),
                    StockImportedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    StockImportedBy = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: true),
                    TopologySummary = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: false),
                    StockSummary = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: true),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WarehouseOpeningImports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WarehouseOpeningImports_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WarehouseAuditEvents_WarehouseOpeningImportId_OccurredAtUtc",
                table: "WarehouseAuditEvents",
                columns: new[] { "WarehouseOpeningImportId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_WarehouseOpeningImports_WarehouseId",
                table: "WarehouseOpeningImports",
                column: "WarehouseId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_WarehouseAuditEvents_WarehouseOpeningImports_WarehouseOpeningImportId",
                table: "WarehouseAuditEvents",
                column: "WarehouseOpeningImportId",
                principalTable: "WarehouseOpeningImports",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WarehouseAuditEvents_WarehouseOpeningImports_WarehouseOpeningImportId",
                table: "WarehouseAuditEvents");

            migrationBuilder.DropTable(
                name: "WarehouseOpeningImports");

            migrationBuilder.DropIndex(
                name: "IX_WarehouseAuditEvents_WarehouseOpeningImportId_OccurredAtUtc",
                table: "WarehouseAuditEvents");

            migrationBuilder.DropColumn(
                name: "WarehouseOpeningImportId",
                table: "WarehouseAuditEvents");
        }
    }
}

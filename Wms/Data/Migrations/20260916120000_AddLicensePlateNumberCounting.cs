using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wms.Data.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260916120000_AddLicensePlateNumberCounting")]
public partial class AddLicensePlateNumberCounting : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "ClosedAtUtc",
            table: "LicensePlateNumbers",
            type: "datetimeoffset",
            nullable: true);
        migrationBuilder.AddColumn<string>(
            name: "ClosedBy",
            table: "LicensePlateNumbers",
            type: "nvarchar(450)",
            maxLength: 450,
            nullable: true);
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "ReopenedAtUtc",
            table: "LicensePlateNumbers",
            type: "datetimeoffset",
            nullable: true);
        migrationBuilder.AddColumn<string>(
            name: "ReopenedBy",
            table: "LicensePlateNumbers",
            type: "nvarchar(450)",
            maxLength: 450,
            nullable: true);

        migrationBuilder.CreateTable(
            name: "LicensePlateNumberItems",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                LicensePlateNumberId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                StockKeepingUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Quantity = table.Column<decimal>(type: "decimal(15,3)", precision: 15, scale: 3, nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                CreatedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                UpdatedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                DeletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                DeletedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_LicensePlateNumberItems", x => x.Id);
                table.ForeignKey(
                    name: "FK_LicensePlateNumberItems_LicensePlateNumbers_LicensePlateNumberId",
                    column: x => x.LicensePlateNumberId,
                    principalTable: "LicensePlateNumbers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_LicensePlateNumberItems_StockKeepingUnits_StockKeepingUnitId",
                    column: x => x.StockKeepingUnitId,
                    principalTable: "StockKeepingUnits",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_LicensePlateNumberItems_StockKeepingUnitId",
            table: "LicensePlateNumberItems",
            column: "StockKeepingUnitId");
        migrationBuilder.CreateIndex(
            name: "UX_LicensePlateNumberItems_ActiveSku",
            table: "LicensePlateNumberItems",
            columns: new[] { "LicensePlateNumberId", "StockKeepingUnitId" },
            unique: true,
            filter: "[DeletedAtUtc] IS NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "LicensePlateNumberItems");
        migrationBuilder.DropColumn(name: "ClosedAtUtc", table: "LicensePlateNumbers");
        migrationBuilder.DropColumn(name: "ClosedBy", table: "LicensePlateNumbers");
        migrationBuilder.DropColumn(name: "ReopenedAtUtc", table: "LicensePlateNumbers");
        migrationBuilder.DropColumn(name: "ReopenedBy", table: "LicensePlateNumbers");
    }
}
